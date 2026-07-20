using UnityEngine;

/// <summary>
/// Persistent, lazily-created singleton for service components.
/// Existing Instance-based call sites remain supported.
/// </summary>
public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    static T instance;
    static bool isQuitting;

    public static bool HasInstance => instance != null;

    public static T Instance
    {
        get
        {
            if (isQuitting)
                return null;

            if (instance == null)
            {
                instance = (T)FindAnyObjectByType(typeof(T), FindObjectsInactive.Include);
                if (instance == null)
                {
                    var obj = new GameObject(typeof(T).Name);
                    instance = obj.AddComponent<T>();
                }
            }

            return instance;
        }
    }

    public static bool TryGetInstance(out T value)
    {
        value = instance;
        return value != null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        isQuitting = false;
    }

    protected virtual void Awake()
    {
        if (instance != null && instance != this as T)
        {
            Destroy(this);
            return;
        }

        instance = this as T;

        // A persistent manager must be a root object. Detach only the manager
        // object instead of preserving an entire scene/XR Origin hierarchy.
        if (transform.parent != null)
            transform.SetParent(null, true);

        DontDestroyOnLoad(gameObject);
    }

    protected virtual void OnDestroy()
    {
        if (instance == this as T)
            instance = null;
    }

    protected virtual void OnApplicationQuit() => isQuitting = true;
}
