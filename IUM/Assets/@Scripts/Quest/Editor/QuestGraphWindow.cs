using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class QuestGraphWindow : EditorWindow
{
    const string QuestDataPath = "Assets/@AddressableAssets/Data/Static/quest.json";

    QuestTable _table;
    QuestDefinition _quest;
    QuestGraphView _graph;
    ScrollView _inspector;
    PopupField<string> _questPicker;
    TextField _newQuestId;
    readonly JsonDataSerializer _serializer = new();

    [MenuItem("Tools/IUM/Quest Graph")]
    public static void Open()
    {
        var window = GetWindow<QuestGraphWindow>();
        window.titleContent = new GUIContent("IUM Quest Graph");
        window.minSize = new Vector2(980f, 600f);
    }

    public void CreateGUI()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.flexDirection = FlexDirection.Column;

        CreateToolbar();

        var split = new TwoPaneSplitView(0, 760f, TwoPaneSplitViewOrientation.Horizontal);
        split.style.flexGrow = 1f;
        rootVisualElement.Add(split);

        _graph = new QuestGraphView(this);
        split.Add(_graph);

        _inspector = new ScrollView();
        _inspector.style.minWidth = 320f;
        _inspector.style.paddingLeft = 10f;
        _inspector.style.paddingRight = 10f;
        split.Add(_inspector);

        LoadTable();
    }

    void CreateToolbar()
    {
        var toolbar = new Toolbar();
        _questPicker = new PopupField<string>("Quest", new List<string> { "<none>" }, 0);
        _questPicker.style.minWidth = 230f;
        _questPicker.RegisterValueChangedCallback(evt => SelectQuest(evt.newValue));
        toolbar.Add(_questPicker);

        _newQuestId = new TextField { value = "new_quest" };
        _newQuestId.style.width = 150f;
        toolbar.Add(_newQuestId);
        toolbar.Add(new ToolbarButton(CreateQuest) { text = "Create" });
        toolbar.Add(new ToolbarButton(Save) { text = "Save" });
        toolbar.Add(new ToolbarButton(Validate) { text = "Validate" });
        toolbar.Add(new ToolbarButton(LoadTable) { text = "Reload" });
        rootVisualElement.Add(toolbar);
    }

    void LoadTable()
    {
        try
        {
            if (!File.Exists(QuestDataPath))
                throw new FileNotFoundException("quest.json을 찾을 수 없습니다.", QuestDataPath);

            _table = _serializer.Deserialize<QuestTable>(File.ReadAllText(QuestDataPath));
            _table.Quests ??= new List<QuestDefinition>();
            _table.Settings ??= new ProcessSettings();

            RefreshQuestPicker(_table.Quests.FirstOrDefault()?.Id);
        }
        catch (Exception exception)
        {
            _table = QuestTable.CreateEmpty();
            _quest = null;
            _graph?.ClearGraph();
            ShowQuestInspector();
            Debug.LogError($"[QuestGraph] {exception.Message}");
        }
    }

    void RefreshQuestPicker(string preferred)
    {
        var choices = _table.Quests
            .Where(quest => quest != null && !string.IsNullOrWhiteSpace(quest.Id))
            .Select(quest => quest.Id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (choices.Count == 0) choices.Add("<none>");
        _questPicker.choices = choices;

        var selected = !string.IsNullOrWhiteSpace(preferred) && choices.Contains(preferred)
            ? preferred
            : choices[0];
        _questPicker.SetValueWithoutNotify(selected);
        SelectQuest(selected, false);
    }

    void SelectQuest(string questId, bool writeCurrent = true)
    {
        if (writeCurrent) WriteCurrentGraph();

        _quest = _table?.Quests?.FirstOrDefault(
            quest => quest != null && string.Equals(quest.Id, questId, StringComparison.OrdinalIgnoreCase));

        _graph.Load(_quest);
        ShowQuestInspector();
    }

    void CreateQuest()
    {
        var id = _newQuestId.value?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            EditorUtility.DisplayDialog("Quest Graph", "퀘스트 ID를 입력하십시오.", "확인");
            return;
        }

        if (_table.Quests.Any(quest => quest != null &&
                                      string.Equals(quest.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            EditorUtility.DisplayDialog("Quest Graph", $"퀘스트 ID '{id}'가 이미 있습니다.", "확인");
            return;
        }

        WriteCurrentGraph();
        var usedProcesses = _table.Quests
            .Where(quest => quest != null)
            .Select(quest => quest.Process)
            .ToHashSet();
        var availableProcess = Enum.GetValues(typeof(ProcessId))
            .Cast<ProcessId>()
            .FirstOrDefault(process => !usedProcesses.Contains(process));
        if (usedProcesses.Contains(availableProcess))
        {
            EditorUtility.DisplayDialog(
                "Quest Graph",
                "모든 ProcessId에 이미 퀘스트가 연결되어 있습니다.",
                "확인");
            return;
        }

        var quest = new QuestDefinition
        {
            Id = id,
            Process = availableProcess,
            EntryNode = "entry",
            Nodes = new List<QuestNodeData>
            {
                new()
                {
                    Id = "entry",
                    Kind = QuestNodeKind.Entry,
                    Position = new QuestNodePosition { X = 60f, Y = 180f }
                },
                new()
                {
                    Id = "complete",
                    Kind = QuestNodeKind.Complete,
                    Position = new QuestNodePosition { X = 420f, Y = 180f }
                }
            },
            Edges = new List<QuestEdgeData>
            {
                new() { From = "entry", To = "complete" }
            }
        };

        _table.Quests.Add(quest);
        RefreshQuestPicker(id);
    }

    void Save()
    {
        if (_table == null) return;
        WriteCurrentGraph();

        var errors = QuestGraphValidator.Validate(_table);
        if (errors.Count > 0)
        {
            EditorUtility.DisplayDialog(
                "Quest Graph Validation Failed",
                string.Join("\n", errors.Take(12)) + (errors.Count > 12 ? $"\n… {errors.Count - 12}개 더 있음" : string.Empty),
                "확인");
            return;
        }

        File.WriteAllText(QuestDataPath, _serializer.Serialize(_table));
        AssetDatabase.ImportAsset(QuestDataPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[QuestGraph] {QuestDataPath} 저장 완료.");
    }

    void Validate()
    {
        if (_table == null) return;
        WriteCurrentGraph();
        var errors = QuestGraphValidator.Validate(_table);

        if (errors.Count == 0)
        {
            EditorUtility.DisplayDialog("Quest Graph", "검증을 통과했습니다.", "확인");
            return;
        }

        foreach (var error in errors) Debug.LogError($"[QuestGraph] {error}");
        EditorUtility.DisplayDialog(
            "Quest Graph Validation Failed",
            string.Join("\n", errors.Take(12)) + (errors.Count > 12 ? $"\n… {errors.Count - 12}개 더 있음" : string.Empty),
            "확인");
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

        AddHeading("Node");
        var id = new TextField("ID") { value = node.Data.Id };
        id.RegisterValueChangedCallback(evt =>
        {
            var value = evt.newValue.Trim();
            if (string.IsNullOrWhiteSpace(value)) return;

            node.Data.Id = value;
            if (node.Data.Objective != null) node.Data.Objective.Id = value;
            if (node.Data.Kind == QuestNodeKind.Entry) _quest.EntryNode = value;
            node.RefreshTitle();
        });
        _inspector.Add(id);
        _inspector.Add(new Label($"Kind: {node.Data.Kind}"));

        if (node.Data.Kind != QuestNodeKind.Objective) return;
        node.Data.Objective ??= new ProcessStepData { Id = node.Data.Id };
        var objective = node.Data.Objective;

        AddHeading("Objective");
        AddEnumField("Condition", objective.Condition, value => objective.Condition = (StepCondition)value);
        AddTextField("Target", objective.Target, value => objective.Target = value);
        AddTextField("Unlock (comma)", string.Join(",", objective.Unlock ?? new List<string>()), value =>
        {
            objective.Unlock = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
        AddFloatField("Amount", objective.Amount, value => objective.Amount = value);
        AddFloatField("Hold Seconds", objective.HoldSeconds, value => objective.HoldSeconds = value);
        AddTextField("Goal", objective.Goal, value => objective.Goal = value, true);

        AddHeading("Dialogue");
        AddTextField("Intro", objective.IntroDialogue, value => objective.IntroDialogue = value);
        AddTextField("Retry", objective.RetryDialogue, value => objective.RetryDialogue = value);
        AddTextField("Success", objective.SuccessDialogue, value => objective.SuccessDialogue = value);
    }

    void ShowQuestInspector()
    {
        if (_inspector == null) return;
        _inspector.Clear();
        AddQuestFields();
    }

    void AddQuestFields()
    {
        AddHeading("Quest");
        if (_quest == null)
        {
            _inspector.Add(new Label("퀘스트를 선택하거나 생성하십시오."));
            return;
        }

        _inspector.Add(new Label($"ID: {_quest.Id}"));
        AddEnumField("Process", _quest.Process, value => _quest.Process = (ProcessId)value);
        AddEnumField("Grade", _quest.Grade, value => _quest.Grade = (ProcessGrade)value);
        AddTextField("Intro Dialogue", _quest.IntroDialogue, value => _quest.IntroDialogue = value);
        AddTextField("Complete Dialogue", _quest.CompleteDialogue, value => _quest.CompleteDialogue = value);
    }

    void AddHeading(string text)
    {
        var heading = new Label(text);
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.marginTop = 10f;
        heading.style.marginBottom = 4f;
        _inspector.Add(heading);
    }

    void AddTextField(string label, string value, Action<string> changed, bool multiline = false)
    {
        var field = new TextField(label) { value = value ?? string.Empty, multiline = multiline };
        field.RegisterValueChangedCallback(evt => changed(evt.newValue));
        _inspector.Add(field);
    }

    void AddFloatField(string label, float value, Action<float> changed)
    {
        var field = new FloatField(label) { value = value };
        field.RegisterValueChangedCallback(evt => changed(evt.newValue));
        _inspector.Add(field);
    }

    void AddEnumField<T>(string label, T value, Action<Enum> changed) where T : Enum
    {
        var field = new EnumField(label, value);
        field.RegisterValueChangedCallback(evt => changed(evt.newValue));
        _inspector.Add(field);
    }
}

sealed class QuestGraphView : GraphView
{
    readonly QuestGraphWindow _window;
    readonly QuestNodeSearchProvider _search;
    QuestDefinition _quest;

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

        _search = ScriptableObject.CreateInstance<QuestNodeSearchProvider>();
        _search.Initialize(this, window);
        nodeCreationRequest = context =>
            SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), _search);
    }

    public void Load(QuestDefinition quest)
    {
        ClearGraph();
        _quest = quest;
        if (quest?.Nodes == null) return;

        var views = new Dictionary<string, QuestNodeView>(StringComparer.OrdinalIgnoreCase);
        foreach (var data in quest.Nodes)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.Id)) continue;
            var view = AddNode(data);
            views[data.Id] = view;
        }

        if (quest.Edges == null) return;
        foreach (var edgeData in quest.Edges)
        {
            if (edgeData == null || !views.TryGetValue(edgeData.From, out var from) ||
                !views.TryGetValue(edgeData.To, out var to) || from.Output == null || to.Input == null)
                continue;

            AddElement(from.Output.ConnectTo(to.Input));
        }
    }

    public void ClearGraph()
    {
        DeleteElements(graphElements.ToList());
        _quest = null;
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

    public QuestNodeView CreateNode(QuestNodeKind kind, Vector2 screenPosition)
    {
        if (_quest == null) return null;
        if (kind == QuestNodeKind.Entry && nodes.ToList().OfType<QuestNodeView>()
                .Any(node => node.Data.Kind == QuestNodeKind.Entry))
        {
            EditorUtility.DisplayDialog("Quest Graph", "Entry 노드는 하나만 만들 수 있습니다.", "확인");
            return null;
        }

        var windowPosition = screenPosition - _window.position.position;
        var graphPosition = contentViewContainer.WorldToLocal(windowPosition);
        var id = UniqueId(kind.ToString().ToLowerInvariant());
        var data = new QuestNodeData
        {
            Id = id,
            Kind = kind,
            Position = new QuestNodePosition { X = graphPosition.x, Y = graphPosition.y },
            Objective = kind == QuestNodeKind.Objective
                ? new ProcessStepData { Id = id, Amount = 1f }
                : null
        };

        return AddNode(data);
    }

    QuestNodeView AddNode(QuestNodeData data)
    {
        var view = new QuestNodeView(data, node => _window.ShowNodeInspector(node));
        view.SetPosition(new Rect(data.Position?.X ?? 0f, data.Position?.Y ?? 0f, 240f, 150f));
        AddElement(view);
        return view;
    }

    string UniqueId(string prefix)
    {
        var used = nodes.ToList().OfType<QuestNodeView>()
            .Select(node => node.Data.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(prefix)) return prefix;

        for (var i = 2; ; i++)
        {
            var candidate = $"{prefix}_{i}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter) =>
        ports.ToList().Where(port => port != startPort && port.node != startPort.node &&
                                     port.direction != startPort.direction).ToList();
}

sealed class QuestNodeView : Node
{
    readonly Action<QuestNodeView> _selected;

    public QuestNodeData Data { get; }
    public Port Input { get; }
    public Port Output { get; }

    public QuestNodeView(QuestNodeData data, Action<QuestNodeView> selected)
    {
        Data = data;
        _selected = selected;
        style.width = 240f;

        if (data.Kind != QuestNodeKind.Entry)
        {
            Input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            Input.portName = "In";
            inputContainer.Add(Input);
        }

        if (data.Kind != QuestNodeKind.Complete)
        {
            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            Output.portName = "Next";
            outputContainer.Add(Output);
        }

        RefreshTitle();
        extensionContainer.Add(new Label(data.Kind.ToString()));
        RefreshExpandedState();
        RefreshPorts();
    }

    public void RefreshTitle() => title = string.IsNullOrWhiteSpace(Data.Id) ? "<unnamed>" : Data.Id;

    public override void OnSelected()
    {
        base.OnSelected();
        _selected?.Invoke(this);
    }
}

sealed class QuestNodeSearchProvider : ScriptableObject, ISearchWindowProvider
{
    QuestGraphView _graph;
    QuestGraphWindow _window;

    public void Initialize(QuestGraphView graph, QuestGraphWindow window)
    {
        _graph = graph;
        _window = window;
    }

    public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context) => new()
    {
        new SearchTreeGroupEntry(new GUIContent("Create Quest Node"), 0),
        Entry("Entry", QuestNodeKind.Entry),
        Entry("Objective", QuestNodeKind.Objective),
        Entry("Complete", QuestNodeKind.Complete)
    };

    static SearchTreeEntry Entry(string name, QuestNodeKind kind) => new(new GUIContent(name))
    {
        level = 1,
        userData = kind
    };

    public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
    {
        if (entry.userData is not QuestNodeKind kind) return false;
        _graph.CreateNode(kind, context.screenMousePosition);
        _window.Focus();
        return true;
    }
}
