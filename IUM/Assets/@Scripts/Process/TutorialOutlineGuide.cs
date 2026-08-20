using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 현재 튜토리얼 단계가 가리키는 ProcessTarget을 QuickOutline으로 강조한다.
/// 공정 판정에는 관여하지 않고 QuestManager 또는 기존 ProcessRunner의 단계 변경 이벤트만 읽는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialOutlineGuide : MonoBehaviour
{
    sealed class HighlightEntry
    {
        public Outline Outline;
        public GameObject Proxy;
    }

    [SerializeField] ProcessRunner runner;
    [SerializeField] QuestManager questManager;
    [SerializeField] Color outlineColor = new(1f, 0.875f, 0f, 1f);
    [SerializeField, Range(0f, 10f)] float outlineWidth = 2f;
    [SerializeField, Range(0f, 1f)] float proxyAlpha = 0.08f;

    readonly Dictionary<ProcessTarget, HighlightEntry> _entries = new();
    readonly HashSet<ProcessTarget> _currentTargets = new();
    readonly List<GameObject> _createdProxies = new();
    readonly List<Material> _createdMaterials = new();
    readonly List<Mesh> _createdMeshes = new();

    void Awake()
    {
        if (questManager == null) questManager = GetComponent<QuestManager>();
        if (runner == null) runner = GetComponent<ProcessRunner>();

        // 씬에 미리 붙여 둔 Outline도 첫 대상 단계 전에는 보이지 않아야 한다.
        var targets = FindObjectsByType<ProcessTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var target in targets)
        {
            if (target == null || target.gameObject.scene != gameObject.scene) continue;

            var existing = target.GetComponent<Outline>();
            if (existing == null) continue;

            existing.enabled = false;
            Configure(existing);
            _entries[target] = new HighlightEntry { Outline = existing };
        }
    }

    void OnEnable()
    {
        if (questManager == null) questManager = GetComponent<QuestManager>();
        if (runner == null) runner = GetComponent<ProcessRunner>();

        if (questManager != null)
        {
            questManager.ObjectiveChanged += HandleObjectiveChanged;
            questManager.Completed += HandleCompleted;
            ApplyStep(questManager.CurrentObjective);
            return;
        }

        if (runner != null)
        {
            runner.StepChanged += HandleStepChanged;
            runner.Completed += HandleCompleted;
            ApplyStep(runner.CurrentStep);
        }
    }

    void Start()
    {
        // 비동기 공정 초기화가 OnEnable과 같은 프레임에 끝난 경우도 현재 단계를 다시 반영한다.
        if (questManager != null) ApplyStep(questManager.CurrentObjective);
        else if (runner != null) ApplyStep(runner.CurrentStep);
    }

    void OnDisable()
    {
        if (questManager != null)
        {
            questManager.ObjectiveChanged -= HandleObjectiveChanged;
            questManager.Completed -= HandleCompleted;
        }

        if (runner != null)
        {
            runner.StepChanged -= HandleStepChanged;
            runner.Completed -= HandleCompleted;
        }

        ClearHighlights();
    }

    void OnDestroy()
    {
        foreach (var proxy in _createdProxies) DestroyCreated(proxy);
        foreach (var material in _createdMaterials) DestroyCreated(material);
        foreach (var mesh in _createdMeshes) DestroyCreated(mesh);
    }

    void HandleStepChanged(ProcessStepData step, int index)
    {
        ApplyStep(step);
    }

    void HandleObjectiveChanged(QuestNodeData node, int index)
    {
        ApplyStep(node?.Objective);
    }

    void HandleCompleted(ProcessId process)
    {
        ClearHighlights();
    }

    void ApplyStep(ProcessStepData step)
    {
        _currentTargets.Clear();

        if (step != null)
        {
            AddTarget(step.Target);

            if (step.Unlock != null)
                foreach (var key in step.Unlock)
                    AddTarget(key);
        }

        foreach (var pair in _entries)
            SetHighlighted(pair.Value, _currentTargets.Contains(pair.Key));

        foreach (var target in _currentTargets)
            SetHighlighted(Resolve(target), true);
    }

    void AddTarget(string key)
    {
        if (!ProcessTarget.TryGet(key, out var target)) return;
        if (target.gameObject.scene != gameObject.scene) return;

        _currentTargets.Add(target);
    }

    HighlightEntry Resolve(ProcessTarget target)
    {
        if (_entries.TryGetValue(target, out var entry)) return entry;

        var outline = target.GetComponent<Outline>();
        GameObject proxy = null;

        if (outline == null && target.GetComponentInChildren<Renderer>(true) == null)
        {
            proxy = CreateProxy(target);
            outline = proxy.AddComponent<Outline>();
        }
        else if (outline == null)
        {
            outline = target.gameObject.AddComponent<Outline>();
        }

        outline.enabled = false;
        Configure(outline);

        entry = new HighlightEntry
        {
            Outline = outline,
            Proxy = proxy
        };
        _entries[target] = entry;
        return entry;
    }

    void Configure(Outline outline)
    {
        outline.OutlineMode = Outline.Mode.OutlineAll;
        outline.OutlineColor = outlineColor;
        outline.OutlineWidth = outlineWidth;
    }

    void ClearHighlights()
    {
        _currentTargets.Clear();
        foreach (var entry in _entries.Values) SetHighlighted(entry, false);
    }

    static void SetHighlighted(HighlightEntry entry, bool value)
    {
        if (entry == null || entry.Outline == null) return;

        if (entry.Proxy != null)
        {
            if (value)
            {
                entry.Proxy.SetActive(true);
                entry.Outline.enabled = true;
            }
            else
            {
                entry.Outline.enabled = false;
                entry.Proxy.SetActive(false);
            }

            return;
        }

        entry.Outline.enabled = value;
    }

    GameObject CreateProxy(ProcessTarget target)
    {
        var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
        proxy.name = "[TutorialOutlineProxy]";
        proxy.hideFlags = HideFlags.DontSave;
        proxy.layer = target.gameObject.layer;
        proxy.transform.SetParent(target.transform, false);

        var box = target.GetComponent<BoxCollider>();
        if (box != null)
        {
            proxy.transform.localPosition = box.center;
            proxy.transform.localScale = box.size;
        }
        else
        {
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localScale = Vector3.one * 0.25f;
        }

        var proxyCollider = proxy.GetComponent<Collider>();
        if (proxyCollider != null)
        {
            proxyCollider.enabled = false;
            DestroyCreated(proxyCollider);
        }

        // QuickOutline이 UV 채널을 기록하므로 공용 내장 Cube 메시지를 복제해 사용한다.
        var filter = proxy.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            var mesh = Instantiate(filter.sharedMesh);
            mesh.name = "Tutorial Outline Proxy Mesh";
            mesh.hideFlags = HideFlags.DontSave;
            filter.sharedMesh = mesh;
            _createdMeshes.Add(mesh);
        }

        var renderer = proxy.GetComponent<Renderer>();
        if (renderer != null)
        {
            var material = CreateProxyMaterial();
            if (material != null)
            {
                renderer.sharedMaterial = material;
                _createdMaterials.Add(material);
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        proxy.SetActive(false);
        _createdProxies.Add(proxy);
        return proxy;
    }

    Material CreateProxyMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("[TutorialOutlineGuide] 안내 프록시용 셰이더를 찾지 못했습니다.", this);
            return null;
        }

        var material = new Material(shader)
        {
            name = "Tutorial Outline Proxy Material",
            hideFlags = HideFlags.DontSave,
            renderQueue = (int)RenderQueue.Transparent
        };

        var color = outlineColor;
        color.a = proxyAlpha;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);

        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return material;
    }

    static void DestroyCreated(Object value)
    {
        if (value == null) return;

        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
