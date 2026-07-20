using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Common gameplay object with module lifecycle support.
/// Legacy 2D component properties are preserved while VR/3D components are cached too.
/// </summary>
public class MonoThing : MonoBehaviour, ISceneEventListener
{
    readonly List<Module> _modules = new();
    float _secondTimer;

    // Existing API
    public Rigidbody2D Rigidbody => rigidbody;
    new protected Rigidbody2D rigidbody;
    public Collider2D Collider => collider;
    new protected Collider2D collider;
    public Animator Animator => animator;
    protected Animator animator;
    public SpriteRenderer SpriteRenderer => spriteRenderer;
    protected SpriteRenderer spriteRenderer;

    // VR/3D API
    public Rigidbody Rigidbody3D => rigidbody3D;
    protected Rigidbody rigidbody3D;
    public Collider Collider3D => collider3D;
    protected Collider collider3D;
    public Renderer Renderer3D => renderer3D;
    protected Renderer renderer3D;

    [HideInInspector] public string AddressableKey;

    protected virtual void Awake()
    {
        rigidbody = GetComponent<Rigidbody2D>();
        collider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        rigidbody3D = GetComponent<Rigidbody>();
        collider3D = GetComponent<Collider>();
        renderer3D = GetComponent<Renderer>();
        animator = GetComponent<Animator>();

        var controller = SceneController.Instance;
        if (controller != null)
            controller.RegisterListener(this);
    }

    protected virtual void OnDestroy()
    {
        if (SceneController.TryGetInstance(out var controller))
            controller.UnregisterListener(this);

        ClearAllModules();
    }

    protected virtual void Update()
    {
        _secondTimer += Time.deltaTime;
        if (_secondTimer >= 1f)
        {
            _secondTimer -= 1f;
            for (var i = 0; i < _modules.Count; i++)
                _modules[i].TickSecond();
        }

        for (var i = 0; i < _modules.Count; i++)
            _modules[i].TickUpdate();
    }

    protected virtual void LateUpdate()
    {
        for (var i = 0; i < _modules.Count; i++)
            _modules[i].TickLateUpdate();
    }

    protected virtual void FixedUpdate()
    {
        for (var i = 0; i < _modules.Count; i++)
            _modules[i].TickFixedUpdate();
    }

    public Module AddModule(Module newModule)
    {
        if (newModule == null)
            return null;

        _modules.Add(newModule);
        newModule.OnAdded();
        return newModule;
    }

    public T GetModule<T>() where T : Module
    {
        for (var i = 0; i < _modules.Count; i++)
            if (_modules[i] is T typed) return typed;
        return null;
    }

    public IEnumerable<T> GetModules<T>() where T : Module
    {
        for (var i = 0; i < _modules.Count; i++)
            if (_modules[i] is T typed) yield return typed;
    }

    public bool TryGetModule<T>(out T module) where T : Module
    {
        module = GetModule<T>();
        return module != null;
    }

    public void RemoveModule<T>() where T : Module
    {
        var module = GetModule<T>();
        if (module == null) return;
        module.Detach();
        _modules.Remove(module);
    }

    public void RemoveAllModules<T>() where T : Module
    {
        for (var i = _modules.Count - 1; i >= 0; i--)
        {
            if (_modules[i] is not T) continue;
            _modules[i].Detach();
            _modules.RemoveAt(i);
        }
    }

    public void ClearAllModules()
    {
        for (var i = _modules.Count - 1; i >= 0; i--)
            _modules[i].Detach();
        _modules.Clear();
    }

    public void OnSceneLoadStart(string sceneName) => ClearAllModules();
    public void OnSceneLoadComplete(string sceneName) { }
}
