/// <summary>Plain-C# behaviour module owned and ticked by a MonoThing.</summary>
public abstract class Module
{
    public MonoThing Thing { get; }
    public bool IsInitialized { get; private set; }

    public Module(MonoThing thing) => Thing = thing;

    public virtual void Init(params object[] objects) => IsInitialized = true;

    public virtual void OnAdded() { }
    public virtual void OnRemoved() { }
    public virtual void OnUpdate() { }
    public virtual void OnLateUpdate() { }
    public virtual void OnFixedUpdate() { }
    public virtual void OnSecond() { }

    internal void TickUpdate()
    {
        if (IsInitialized) OnUpdate();
    }

    internal void TickLateUpdate()
    {
        if (IsInitialized) OnLateUpdate();
    }

    internal void TickFixedUpdate()
    {
        if (IsInitialized) OnFixedUpdate();
    }

    internal void TickSecond()
    {
        if (IsInitialized) OnSecond();
    }

    internal void Detach()
    {
        OnRemoved();
        IsInitialized = false;
    }
}
