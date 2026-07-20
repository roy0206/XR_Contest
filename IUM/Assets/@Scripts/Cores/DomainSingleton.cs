using UnityEngine;

/// <summary>
/// Scene/domain-scoped singleton. Unlike Singleton, it is never made persistent.
/// </summary>
public abstract class DomainSingleton<T> : MonoBehaviour where T : DomainSingleton<T>
{
    public static T Current { get; private set; }

    protected virtual void Awake()
    {
        if (Current != null && Current != this)
        {
            Debug.LogWarning($"[{typeof(T).Name}] Duplicate component was removed.", this);
            Destroy(this);
            return;
        }

        Current = (T)this;
    }

    protected virtual void OnDestroy()
    {
        if (Current == this)
            Current = null;
    }
}
