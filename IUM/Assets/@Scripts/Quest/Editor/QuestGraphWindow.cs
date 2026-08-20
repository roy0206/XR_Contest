using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 선형 퀘스트를 JSON 구조를 직접 다루지 않고 제작하는 편집기. Entry와 Complete는 편집기가
/// 보존하고, 기획자는 그 사이의 목표와 표시 문구에 집중한다.
/// </summary>
public sealed class QuestGraphWindow : EditorWindow
{
    const string QuestDataPath = "Assets/@AddressableAssets/Data/Static/quest.json";
    const string None = "<none>";

    readonly JsonDataSerializer _serializer = new();
    readonly List<QuestDefinition> _filteredQuests = new();

    QuestTable _table;
    QuestDefinition _quest;
    QuestGraphView _graph;
    ScrollView _questList;
    ScrollView _inspector;
    Label _status;
    Label _validation;
    TextField _search;
    TextField _newQuestId;
    bool _dirty;

    [MenuItem("Tools/IUM/Quest Graph")]
    public static void Open()
    {
        var window = GetWindow<QuestGraphWindow>();
        window.titleContent = new GUIContent("IUM Quest Graph");
        window.minSize = new Vector2(1180f, 680f);
    }

    public void CreateGUI()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.flexDirection = FlexDirection.Column;
        rootVisualElement.RegisterCallback<KeyDownEvent>(HandleShortcut);

        CreateToolbar();
        CreateWorkspace();
        LoadTable(false);
    }

    void CreateToolbar()
    {
        var toolbar = new Toolbar();
        toolbar.Add(new ToolbarButton(InsertObjective) { text = "+ 목표" });
        toolbar.Add(new ToolbarButton(AutoLayout) { text = "자동 정렬" });
        toolbar.Add(new ToolbarButton(() => _graph?.FrameAll()) { text = "전체 보기" });
        toolbar.Add(new ToolbarSpacer { flex = true });
        toolbar.Add(new ToolbarButton(ValidateGraph) { text = "검증" });
        toolbar.Add(new ToolbarButton(() => LoadTable(true)) { text = "되돌리기" });
        toolbar.Add(new ToolbarButton(Save) { text = "저장  Ctrl+S" });

        _status = new Label("불러오는 중");
        _status.style.minWidth = 120f;
        _status.style.unityTextAlign = TextAnchor.MiddleRight;
        _status.style.marginLeft = 12f;
        _status.style.marginRight = 8f;
        toolbar.Add(_status);
        rootVisualElement.Add(toolbar);
    }

    void CreateWorkspace()
    {
        var outer = new TwoPaneSplitView(0, 230f, TwoPaneSplitViewOrientation.Horizontal);
        outer.style.flexGrow = 1f;
        rootVisualElement.Add(outer);

        outer.Add(CreateQuestSidebar());

        var content = new TwoPaneSplitView(1, 360f, TwoPaneSplitViewOrientation.Horizontal);
        content.style.flexGrow = 1f;
        outer.Add(content);

        _graph = new QuestGraphView(this);
        content.Add(_graph);

        var inspectorHost = new VisualElement();
        inspectorHost.style.minWidth = 320f;
        inspectorHost.style.flexDirection = FlexDirection.Column;

        _validation = new Label();
        _validation.style.display = DisplayStyle.None;
        _validation.style.whiteSpace = WhiteSpace.Normal;
        _validation.style.paddingLeft = 10f;
        _validation.style.paddingRight = 10f;
        _validation.style.paddingTop = 8f;
        _validation.style.paddingBottom = 8f;
        _validation.style.backgroundColor = new Color(0.35f, 0.13f, 0.13f, 0.9f);
        _validation.style.color = new Color(1f, 0.78f, 0.72f);
        inspectorHost.Add(_validation);

        _inspector = new ScrollView();
        _inspector.style.flexGrow = 1f;
        _inspector.style.paddingLeft = 12f;
        _inspector.style.paddingRight = 12f;
        inspectorHost.Add(_inspector);
        content.Add(inspectorHost);
    }

    VisualElement CreateQuestSidebar()
    {
        var sidebar = new VisualElement();
        sidebar.style.paddingLeft = 8f;
        sidebar.style.paddingRight = 8f;
        sidebar.style.paddingTop = 8f;
        sidebar.style.paddingBottom = 8f;

        var heading = new Label("퀘스트");
        heading.style.fontSize = 15f;
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.marginBottom = 6f;
        sidebar.Add(heading);

        _search = new TextField("검색") { tooltip = "ID, 제목 또는 Process 검색" };
        _search.value = string.Empty;
        _search.RegisterValueChangedCallback(_ => RefreshQuestList());
        sidebar.Add(_search);

        _questList = new ScrollView();
        _questList.style.flexGrow = 1f;
        _questList.style.marginTop = 6f;
        sidebar.Add(_questList);

        var divider = new VisualElement();
        divider.style.height = 1f;
        divider.style.marginTop = 8f;
        divider.style.marginBottom = 8f;
        divider.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        sidebar.Add(divider);

        _newQuestId = new TextField("새 ID") { value = "new_quest" };
        sidebar.Add(_newQuestId);

        var create = new Button(CreateQuest) { text = "새 퀘스트" };
        create.style.marginTop = 5f;
        sidebar.Add(create);

        var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        var duplicate = new Button(DuplicateQuest) { text = "복제" };
        duplicate.style.flexGrow = 1f;
        var delete = new Button(DeleteQuest) { text = "삭제" };
        delete.style.flexGrow = 1f;
        row.Add(duplicate);
        row.Add(delete);
        sidebar.Add(row);
        return sidebar;
    }

    void LoadTable(bool confirmDiscard)
    {
        if (confirmDiscard && _dirty &&
            !EditorUtility.DisplayDialog("Quest Graph", "저장하지 않은 변경을 버리고 다시 불러오시겠습니까?", "되돌리기", "취소"))
            return;

        try
        {
            if (!File.Exists(QuestDataPath))
                throw new FileNotFoundException("quest.json을 찾을 수 없습니다.", QuestDataPath);

            _table = _serializer.Deserialize<QuestTable>(File.ReadAllText(QuestDataPath));
            _table.Quests ??= new List<QuestDefinition>();
            _table.Settings ??= new ProcessSettings();

            var preferred = _quest?.Id ?? _table.Quests.FirstOrDefault()?.Id;
            _dirty = false;
            SelectQuest(preferred, false);
            RefreshQuestList();
            RefreshStatus("불러옴");
            RefreshValidation(false);
        }
        catch (Exception exception)
        {
            _table = QuestTable.CreateEmpty();
            _quest = null;
            _graph?.ClearGraph();
            RefreshQuestList();
            ShowQuestInspector();
            RefreshStatus("불러오기 실패");
            ShowValidation(new[] { exception.Message });
            Debug.LogError($"[QuestGraph] {exception.Message}");
        }
    }

    void RefreshQuestList()
    {
        if (_questList == null) return;
        _questList.Clear();
        _filteredQuests.Clear();

        var query = _search?.value?.Trim();
        if (_table?.Quests != null)
        {
            _filteredQuests.AddRange(_table.Quests
                .Where(quest => quest != null && Matches(quest, query))
                .OrderBy(quest => quest.Id, StringComparer.OrdinalIgnoreCase));
        }

        foreach (var quest in _filteredQuests)
        {
            var title = string.IsNullOrWhiteSpace(quest.Title) ? quest.Id : quest.Title;
            var button = new Button(() => SelectQuest(quest.Id))
            {
                text = $"{title}\n{quest.Id}  ·  {quest.Process}"
            };
            button.style.height = 48f;
            button.style.marginBottom = 4f;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.whiteSpace = WhiteSpace.Normal;
            if (ReferenceEquals(quest, _quest))
            {
                button.style.backgroundColor = new Color(0.15f, 0.38f, 0.55f);
                button.style.color = Color.white;
            }
            _questList.Add(button);
        }

        if (_filteredQuests.Count == 0)
        {
            var empty = new Label("검색 결과가 없습니다.");
            empty.style.marginTop = 10f;
            empty.style.color = new Color(0.55f, 0.55f, 0.55f);
            _questList.Add(empty);
        }
    }

    static bool Matches(QuestDefinition quest, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return Contains(quest.Id, query) || Contains(quest.Title, query) || Contains(quest.Process.ToString(), query);
    }

    static bool Contains(string value, string query) =>
        !string.IsNullOrWhiteSpace(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    void SelectQuest(string questId, bool writeCurrent = true)
    {
        if (writeCurrent) WriteCurrentGraph();

        _quest = _table?.Quests?.FirstOrDefault(
            quest => quest != null && string.Equals(quest.Id, questId, StringComparison.OrdinalIgnoreCase));

        _graph?.Load(_quest);
        ShowQuestInspector();
        RefreshQuestList();
        RefreshValidation(false);
    }

    void CreateQuest()
    {
        if (_table == null) return;
        var id = _newQuestId.value?.Trim();
        if (!CanUseQuestId(id)) return;
        if (!TryGetAvailableProcess(out var process)) return;

        WriteCurrentGraph();
        var quest = CreateEmptyQuest(id, process);
        _table.Quests.Add(quest);
        _quest = quest;
        _graph.Load(quest);
        MarkDirty("새 퀘스트");
        ShowQuestInspector();
        RefreshQuestList();
    }

    void DuplicateQuest()
    {
        if (_quest == null) return;
        if (!TryGetAvailableProcess(out var process)) return;

        WriteCurrentGraph();
        var clone = _serializer.Deserialize<QuestDefinition>(_serializer.Serialize(_quest));
        clone.Id = UniqueQuestId($"{_quest.Id}_copy");
        clone.Title = string.IsNullOrWhiteSpace(_quest.Title) ? "복제 퀘스트" : $"{_quest.Title} 복사본";
        clone.Process = process;
        _table.Quests.Add(clone);
        _quest = clone;
        _graph.Load(clone);
        MarkDirty("퀘스트 복제");
        ShowQuestInspector();
        RefreshQuestList();
    }

    void DeleteQuest()
    {
        if (_quest == null) return;
        if (!EditorUtility.DisplayDialog("Quest Graph", $"퀘스트 '{_quest.Id}'를 삭제하시겠습니까?", "삭제", "취소"))
            return;

        var index = _table.Quests.IndexOf(_quest);
        _table.Quests.Remove(_quest);
        _quest = _table.Quests.Count == 0 ? null : _table.Quests[Mathf.Clamp(index, 0, _table.Quests.Count - 1)];
        _graph.Load(_quest);
        MarkDirty("퀘스트 삭제");
        ShowQuestInspector();
        RefreshQuestList();
    }

    bool CanUseQuestId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            EditorUtility.DisplayDialog("Quest Graph", "퀘스트 ID를 입력하십시오.", "확인");
            return false;
        }

        if (_table.Quests.Any(quest => quest != null &&
                                      string.Equals(quest.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            EditorUtility.DisplayDialog("Quest Graph", $"퀘스트 ID '{id}'가 이미 있습니다.", "확인");
            return false;
        }

        return true;
    }

    bool TryGetAvailableProcess(out ProcessId process)
    {
        var used = _table.Quests.Where(quest => quest != null).Select(quest => quest.Process).ToHashSet();
        foreach (ProcessId candidate in Enum.GetValues(typeof(ProcessId)))
        {
            if (used.Contains(candidate)) continue;
            process = candidate;
            return true;
        }

        process = default;
        EditorUtility.DisplayDialog("Quest Graph", "모든 ProcessId에 이미 퀘스트가 연결되어 있습니다.", "확인");
        return false;
    }

    static QuestDefinition CreateEmptyQuest(string id, ProcessId process) => new()
    {
        Id = id,
        Title = "새 퀘스트",
        Process = process,
        EntryNode = "entry",
        Nodes = new List<QuestNodeData>
        {
            new()
            {
                Id = "entry",
                Kind = QuestNodeKind.Entry,
                Position = new QuestNodePosition { X = 80f, Y = 220f }
            },
            new()
            {
                Id = "complete",
                Kind = QuestNodeKind.Complete,
                Position = new QuestNodePosition { X = 440f, Y = 220f }
            }
        },
        Edges = new List<QuestEdgeData> { new() { From = "entry", To = "complete" } }
    };

    string UniqueQuestId(string prefix)
    {
        var candidate = prefix;
        for (var number = 2; _table.Quests.Any(quest => quest != null &&
                                                       string.Equals(quest.Id, candidate, StringComparison.OrdinalIgnoreCase)); number++)
            candidate = $"{prefix}_{number}";
        return candidate;
    }

    void InsertObjective()
    {
        if (_quest == null) return;
        WriteCurrentGraph();

        var complete = _quest.Nodes?.FirstOrDefault(node => node?.Kind == QuestNodeKind.Complete);
        var incomingCandidates = complete == null
            ? Array.Empty<QuestEdgeData>()
            : (_quest.Edges ?? new List<QuestEdgeData>()).Where(edge => edge?.To == complete.Id).ToArray();
        if (complete == null || incomingCandidates.Length != 1)
        {
            EditorUtility.DisplayDialog("Quest Graph", "Complete로 이어지는 단일 경로를 먼저 복구하십시오.", "확인");
            return;
        }

        var incoming = incomingCandidates[0];
        var before = _quest.Nodes.FirstOrDefault(node => node?.Id == incoming.From);
        var id = UniqueNodeId("objective");
        var data = new QuestNodeData
        {
            Id = id,
            Kind = QuestNodeKind.Objective,
            ControlHint = "플레이어에게 보여 줄 조작 안내를 입력하세요.",
            Position = new QuestNodePosition
            {
                X = ((before?.Position?.X ?? complete.Position?.X - 320f ?? 80f) + (complete.Position?.X ?? 440f)) * 0.5f,
                Y = complete.Position?.Y ?? 220f
            },
            Objective = new ProcessStepData
            {
                Id = id,
                Amount = 1f,
                Goal = "새 목표"
            }
        };

        _quest.Nodes.Add(data);
        _quest.Edges.Remove(incoming);
        _quest.Edges.Add(new QuestEdgeData { From = incoming.From, To = id });
        _quest.Edges.Add(new QuestEdgeData { From = id, To = complete.Id });
        _graph.Load(_quest);
        _graph.SelectNode(id);
        AutoLayout();
        MarkDirty("목표 추가");
    }

    internal void DeleteObjective(QuestNodeView view)
    {
        if (_quest == null || view?.Data?.Kind != QuestNodeKind.Objective) return;
        if (!EditorUtility.DisplayDialog("Quest Graph", $"목표 '{view.Data.Id}'를 삭제하시겠습니까?", "삭제", "취소"))
            return;

        WriteCurrentGraph();
        var incoming = _quest.Edges.Where(edge => edge?.To == view.Data.Id).ToArray();
        var outgoing = _quest.Edges.Where(edge => edge?.From == view.Data.Id).ToArray();
        if (incoming.Length != 1 || outgoing.Length != 1)
        {
            EditorUtility.DisplayDialog("Quest Graph", "목표의 앞뒤 연결이 하나씩이어야 안전하게 삭제할 수 있습니다.", "확인");
            return;
        }

        _quest.Edges.Remove(incoming[0]);
        _quest.Edges.Remove(outgoing[0]);
        _quest.Edges.Add(new QuestEdgeData { From = incoming[0].From, To = outgoing[0].To });
        _quest.Nodes.Remove(view.Data);
        _graph.Load(_quest);
        AutoLayout();
        MarkDirty("목표 삭제");
        ShowQuestInspector();
    }

    string UniqueNodeId(string prefix)
    {
        var used = (_quest.Nodes ?? new List<QuestNodeData>())
            .Where(node => node != null)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(prefix)) return prefix;
        for (var i = 2; ; i++)
        {
            var candidate = $"{prefix}_{i}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    void AutoLayout()
    {
        if (_quest == null || _graph == null) return;
        WriteCurrentGraph();
        _graph.AutoLayout(_quest);
        MarkDirty("자동 정렬");
    }

    void Save()
    {
        if (_table == null) return;
        WriteCurrentGraph();
        var errors = QuestGraphValidator.Validate(_table);
        ShowValidation(errors);
        if (errors.Count > 0)
        {
            RefreshStatus($"오류 {errors.Count}개");
            return;
        }

        File.WriteAllText(QuestDataPath, _serializer.Serialize(_table));
        AssetDatabase.ImportAsset(QuestDataPath, ImportAssetOptions.ForceUpdate);
        _dirty = false;
        RefreshStatus("저장됨");
        Debug.Log($"[QuestGraph] {QuestDataPath} 저장 완료.");
    }

    void ValidateGraph()
    {
        RefreshValidation(true);
        if (_validation.style.display == DisplayStyle.None)
            RefreshStatus("검증 통과");
    }

    void RefreshValidation(bool logErrors)
    {
        if (_table == null) return;
        WriteCurrentGraph();
        var errors = QuestGraphValidator.Validate(_table);
        ShowValidation(errors);
        if (!logErrors) return;
        foreach (var error in errors) Debug.LogError($"[QuestGraph] {error}");
    }

    void ShowValidation(IEnumerable<string> source)
    {
        if (_validation == null) return;
        var errors = source?.ToList() ?? new List<string>();
        if (errors.Count == 0)
        {
            _validation.text = string.Empty;
            _validation.style.display = DisplayStyle.None;
            return;
        }

        _validation.text = "저장할 수 없습니다\n\n" + string.Join("\n", errors.Take(8).Select(error => $"• {error}")) +
                           (errors.Count > 8 ? $"\n• 그 외 {errors.Count - 8}개" : string.Empty);
        _validation.style.backgroundColor = new Color(0.35f, 0.13f, 0.13f, 0.9f);
        _validation.style.color = new Color(1f, 0.78f, 0.72f);
        _validation.style.display = DisplayStyle.Flex;
    }

    internal void MarkDirty(string reason = null)
    {
        _dirty = true;
        RefreshStatus(string.IsNullOrWhiteSpace(reason) ? "수정됨" : $"수정됨 · {reason}");
        if (_validation != null)
        {
            _validation.text = "저장 전 검증이 필요합니다.";
            _validation.style.display = DisplayStyle.Flex;
            _validation.style.backgroundColor = new Color(0.24f, 0.2f, 0.08f, 0.9f);
            _validation.style.color = new Color(1f, 0.88f, 0.55f);
        }
    }

    void RefreshStatus(string state)
    {
        if (_status == null) return;
        _status.text = _dirty ? $"● {state}" : state;
        _status.style.color = _dirty ? new Color(1f, 0.75f, 0.25f) : new Color(0.65f, 0.85f, 0.7f);
    }

    void WriteCurrentGraph()
    {
        if (_quest == null || _graph == null) return;
        _graph.WriteTo(_quest);
    }

    internal void ShowNodeInspector(QuestNodeView node)
    {
        _inspector.Clear();
        AddQuestFields();
        if (node == null) return;

        AddHeading("선택한 노드");
        var id = new TextField("노드 ID") { value = node.Data.Id };
        id.SetEnabled(node.Data.Kind == QuestNodeKind.Objective);
        id.RegisterValueChangedCallback(evt =>
        {
            var value = evt.newValue.Trim();
            if (string.IsNullOrWhiteSpace(value)) return;
            node.Data.Id = value;
            if (node.Data.Objective != null) node.Data.Objective.Id = value;
            node.RefreshContent();
            MarkDirty("노드 ID");
        });
        _inspector.Add(id);
        _inspector.Add(new Label($"종류: {KindName(node.Data.Kind)}"));

        if (node.Data.Kind != QuestNodeKind.Objective) return;
        node.Data.Objective ??= new ProcessStepData { Id = node.Data.Id, Amount = 1f };
        var objective = node.Data.Objective;

        AddHeading("플레이어 표시");
        AddTextField("현재 목표", objective.Goal, value =>
        {
            objective.Goal = value;
            node.RefreshContent();
        }, true);
        AddTextField("조작 힌트", node.Data.ControlHint, value =>
        {
            node.Data.ControlHint = value;
            node.RefreshContent();
        }, true);

        AddHeading("완료 조건");
        AddEnumField("조건", objective.Condition, value =>
        {
            objective.Condition = (StepCondition)value;
            node.RefreshContent();
        });
        AddTextField("대상 키", objective.Target, value =>
        {
            objective.Target = value;
            node.RefreshContent();
        });
        AddTextField("허용 대상 (쉼표)", string.Join(",", objective.Unlock ?? new List<string>()), value =>
        {
            objective.Unlock = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
        AddFloatField("요구량", objective.Amount, value => objective.Amount = value);
        AddFloatField("유지 시간", objective.HoldSeconds, value => objective.HoldSeconds = value);

        AddHeading("대사 연결");
        AddTextField("시작 대사", objective.IntroDialogue, value => objective.IntroDialogue = value);
        AddTextField("재안내 대사", objective.RetryDialogue, value => objective.RetryDialogue = value);
        AddTextField("성공 대사", objective.SuccessDialogue, value => objective.SuccessDialogue = value);

        var delete = new Button(() => DeleteObjective(node)) { text = "이 목표 삭제" };
        delete.style.marginTop = 18f;
        delete.style.height = 30f;
        delete.style.backgroundColor = new Color(0.45f, 0.14f, 0.14f);
        _inspector.Add(delete);
    }

    void ShowQuestInspector()
    {
        if (_inspector == null) return;
        _inspector.Clear();
        AddQuestFields();
        if (_quest != null)
        {
            var help = new Label("노드를 선택하면 목표·조건·대사를 편집할 수 있습니다.");
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.marginTop = 14f;
            help.style.color = new Color(0.55f, 0.62f, 0.7f);
            _inspector.Add(help);
        }
    }

    void AddQuestFields()
    {
        AddHeading("퀘스트 설정");
        if (_quest == null)
        {
            _inspector.Add(new Label("왼쪽에서 퀘스트를 선택하거나 생성하십시오."));
            return;
        }

        var id = new TextField("퀘스트 ID") { value = _quest.Id };
        id.SetEnabled(false);
        _inspector.Add(id);
        AddTextField("표시 제목", _quest.Title, value =>
        {
            _quest.Title = value;
            RefreshQuestList();
        });
        AddEnumField("진행 Process", _quest.Process, value => _quest.Process = (ProcessId)value);
        AddEnumField("완료 등급", _quest.Grade, value => _quest.Grade = (ProcessGrade)value);
        AddTextField("진입 대사", _quest.IntroDialogue, value => _quest.IntroDialogue = value);
        AddTextField("완료 대사", _quest.CompleteDialogue, value => _quest.CompleteDialogue = value);
    }

    void AddHeading(string text)
    {
        var heading = new Label(text);
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.fontSize = 14f;
        heading.style.marginTop = 12f;
        heading.style.marginBottom = 6f;
        _inspector.Add(heading);
    }

    void AddTextField(string label, string value, Action<string> changed, bool multiline = false)
    {
        var field = new TextField(label) { value = value ?? string.Empty, multiline = multiline };
        field.RegisterValueChangedCallback(evt =>
        {
            changed(evt.newValue);
            MarkDirty(label);
        });
        _inspector.Add(field);
    }

    void AddFloatField(string label, float value, Action<float> changed)
    {
        var field = new FloatField(label) { value = value };
        field.RegisterValueChangedCallback(evt =>
        {
            changed(evt.newValue);
            MarkDirty(label);
        });
        _inspector.Add(field);
    }

    void AddEnumField<T>(string label, T value, Action<Enum> changed) where T : Enum
    {
        var field = new EnumField(label, value);
        field.RegisterValueChangedCallback(evt =>
        {
            changed(evt.newValue);
            MarkDirty(label);
        });
        _inspector.Add(field);
    }

    void HandleShortcut(KeyDownEvent evt)
    {
        if (!(evt.ctrlKey || evt.commandKey) || evt.keyCode != KeyCode.S) return;
        Save();
        evt.StopPropagation();
    }

    static string KindName(QuestNodeKind kind) => kind switch
    {
        QuestNodeKind.Entry => "시작",
        QuestNodeKind.Objective => "목표",
        QuestNodeKind.Complete => "완료",
        _ => kind.ToString()
    };
}

sealed class QuestGraphView : GraphView
{
    readonly QuestGraphWindow _window;
    QuestDefinition _quest;
    bool _loading;

    public QuestGraphView(QuestGraphWindow window)
    {
        _window = window;
        style.flexGrow = 1f;

        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();

        graphViewChanged = change =>
        {
            if (!_loading && (change.edgesToCreate?.Count > 0 || change.elementsToRemove?.Count > 0 ||
                              change.movedElements?.Count > 0))
                _window.MarkDirty("그래프");
            return change;
        };
    }

    public void Load(QuestDefinition quest)
    {
        _loading = true;
        ClearGraph();
        _quest = quest;
        if (quest?.Nodes != null)
        {
            var views = new Dictionary<string, QuestNodeView>(StringComparer.OrdinalIgnoreCase);
            foreach (var data in quest.Nodes)
            {
                if (data == null || string.IsNullOrWhiteSpace(data.Id)) continue;
                var view = AddNode(data);
                views[data.Id] = view;
            }

            if (quest.Edges != null)
            {
                foreach (var edgeData in quest.Edges)
                {
                    if (edgeData == null || !views.TryGetValue(edgeData.From, out var from) ||
                        !views.TryGetValue(edgeData.To, out var to) || from.Output == null || to.Input == null)
                        continue;
                    AddElement(from.Output.ConnectTo(to.Input));
                }
            }
        }
        _loading = false;
    }

    public void ClearGraph()
    {
        var wasLoading = _loading;
        _loading = true;
        DeleteElements(graphElements.ToList());
        _quest = null;
        _loading = wasLoading;
    }

    public void WriteTo(QuestDefinition quest)
    {
        var nodeViews = nodes.ToList().OfType<QuestNodeView>().ToList();
        quest.Nodes = nodeViews.Select(view =>
        {
            var position = view.GetPosition().position;
            view.Data.Position ??= new QuestNodePosition();
            view.Data.Position.X = position.x;
            view.Data.Position.Y = position.y;
            return view.Data;
        }).ToList();

        quest.Edges = edges.ToList()
            .Where(edge => edge.output?.node is QuestNodeView && edge.input?.node is QuestNodeView)
            .Select(edge => new QuestEdgeData
            {
                From = ((QuestNodeView)edge.output.node).Data.Id,
                To = ((QuestNodeView)edge.input.node).Data.Id
            })
            .ToList();

        var entry = quest.Nodes.FirstOrDefault(node => node.Kind == QuestNodeKind.Entry);
        quest.EntryNode = entry?.Id;
    }

    public void AutoLayout(QuestDefinition quest)
    {
        if (quest == null) return;
        var views = nodes.ToList().OfType<QuestNodeView>()
            .ToDictionary(view => view.Data.Id, StringComparer.OrdinalIgnoreCase);
        var edgesByFrom = (quest.Edges ?? new List<QuestEdgeData>())
            .Where(edge => edge != null && !string.IsNullOrWhiteSpace(edge.From))
            .GroupBy(edge => edge.From, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var ordered = new List<QuestNodeView>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = quest.EntryNode;
        while (!string.IsNullOrWhiteSpace(current) && views.TryGetValue(current, out var view) && visited.Add(current))
        {
            ordered.Add(view);
            current = edgesByFrom.TryGetValue(current, out var edge) ? edge.To : null;
        }

        ordered.AddRange(views.Values.Where(view => !visited.Contains(view.Data.Id))
            .OrderBy(view => view.Data.Kind).ThenBy(view => view.Data.Id, StringComparer.OrdinalIgnoreCase));

        _loading = true;
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].SetPosition(new Rect(80f + i * 310f, 220f, 260f, 190f));
        _loading = false;
        FrameAll();
    }

    public void SelectNode(string nodeId)
    {
        var view = nodes.ToList().OfType<QuestNodeView>()
            .FirstOrDefault(node => string.Equals(node.Data.Id, nodeId, StringComparison.OrdinalIgnoreCase));
        if (view == null) return;
        ClearSelection();
        AddToSelection(view);
        _window.ShowNodeInspector(view);
    }

    QuestNodeView AddNode(QuestNodeData data)
    {
        var view = new QuestNodeView(data, node => _window.ShowNodeInspector(node));
        view.SetPosition(new Rect(data.Position?.X ?? 0f, data.Position?.Y ?? 0f, 260f, 190f));
        AddElement(view);
        return view;
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter) =>
        ports.ToList().Where(port => port != startPort && port.node != startPort.node &&
                                     port.direction != startPort.direction).ToList();
}

sealed class QuestNodeView : Node
{
    readonly Action<QuestNodeView> _selected;
    readonly Label _kind;
    readonly Label _goal;
    readonly Label _detail;
    readonly Label _hint;

    public QuestNodeData Data { get; }
    public Port Input { get; }
    public Port Output { get; }

    public QuestNodeView(QuestNodeData data, Action<QuestNodeView> selected)
    {
        Data = data;
        _selected = selected;
        style.width = 260f;
        capabilities &= ~Capabilities.Deletable;
        capabilities &= ~Capabilities.Copiable;

        if (data.Kind != QuestNodeKind.Entry)
        {
            Input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(bool));
            Input.portName = "이전";
            inputContainer.Add(Input);
        }

        if (data.Kind != QuestNodeKind.Complete)
        {
            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            Output.portName = "다음";
            outputContainer.Add(Output);
        }

        _kind = new Label();
        _kind.style.fontSize = 10f;
        _kind.style.unityFontStyleAndWeight = FontStyle.Bold;
        _goal = new Label { style = { whiteSpace = WhiteSpace.Normal, fontSize = 14f } };
        _detail = new Label { style = { whiteSpace = WhiteSpace.Normal, fontSize = 11f } };
        _hint = new Label { style = { whiteSpace = WhiteSpace.Normal, fontSize = 11f } };
        _hint.style.color = new Color(0.65f, 0.78f, 0.9f);
        _hint.style.marginTop = 5f;

        extensionContainer.Add(_kind);
        extensionContainer.Add(_goal);
        extensionContainer.Add(_detail);
        extensionContainer.Add(_hint);
        RefreshContent();
        RefreshExpandedState();
        RefreshPorts();
    }

    public void RefreshContent()
    {
        title = string.IsNullOrWhiteSpace(Data.Id) ? "<ID 없음>" : Data.Id;
        _kind.text = Data.Kind switch
        {
            QuestNodeKind.Entry => "START",
            QuestNodeKind.Objective => "OBJECTIVE",
            QuestNodeKind.Complete => "COMPLETE",
            _ => Data.Kind.ToString().ToUpperInvariant()
        };

        switch (Data.Kind)
        {
            case QuestNodeKind.Entry:
                _goal.text = "퀘스트 시작";
                _detail.text = "진입 대사 후 첫 목표로 이동";
                _hint.text = string.Empty;
                titleContainer.style.backgroundColor = new Color(0.12f, 0.35f, 0.26f);
                break;
            case QuestNodeKind.Complete:
                _goal.text = "퀘스트 완료";
                _detail.text = "완료 보고 후 GameFlow로 복귀";
                _hint.text = string.Empty;
                titleContainer.style.backgroundColor = new Color(0.38f, 0.25f, 0.08f);
                break;
            default:
                var objective = Data.Objective;
                _goal.text = string.IsNullOrWhiteSpace(objective?.Goal) ? "목표 문구 없음" : objective.Goal;
                _detail.text = objective == null
                    ? "조건 데이터 없음"
                    : $"{objective.Condition}  ·  대상 {Value(objective.Target)}  ·  요구량 {objective.Amount:0.##}";
                _hint.text = string.IsNullOrWhiteSpace(Data.ControlHint) ? "조작 힌트 없음" : Data.ControlHint;
                titleContainer.style.backgroundColor = new Color(0.12f, 0.29f, 0.45f);
                break;
        }
    }

    public override void OnSelected()
    {
        base.OnSelected();
        _selected?.Invoke(this);
    }

    static string Value(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
}
