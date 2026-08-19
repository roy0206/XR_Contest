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
            // 살아 있으면 종료 중이라도 돌려준다. isQuitting을 먼저 보면 HasInstance가 true인데
            // Instance가 null인 구간이 생기고, 두 값을 함께 쓰는 호출부가 그 자리에서 NRE를 낸다.
            //
            // OnApplicationQuit은 모든 MonoBehaviour에서 호출되며 순서가 정해져 있지 않다. 어떤
            // 싱글턴이 먼저 플래그를 세우면 아직 살아 있는 그 인스턴스를 다른 컴포넌트가 쓰지
            // 못하게 되는데, 종료 시점의 마지막 저장처럼 그때 반드시 해야 하는 일이 있다.
            //
            // 파괴된 컴포넌트는 Unity의 == 오버로드가 null로 판정하므로 아래 경로로 빠진다.
            if (instance != null) return instance;

            // isQuitting의 본래 목적은 여기다 — 종료 중에 싱글턴을 새로 만들지 않는 것.
            if (isQuitting) return null;

            instance = (T)FindAnyObjectByType(typeof(T), FindObjectsInactive.Include);
            if (instance == null)
            {
                var obj = new GameObject(typeof(T).Name);
                instance = obj.AddComponent<T>();
            }

            return instance;
        }
    }

    public static bool TryGetInstance(out T value)
    {
        value = instance;
        return value != null;
    }

    protected virtual void Awake()
    {
        if (instance != null && instance != this as T)
        {
            Destroy(this);
            return;
        }

        // Entering play mode with domain reload disabled keeps statics alive, so the quit flag from
        // the previous session has to be cleared here. An initialize-on-load hook cannot do it:
        // Unity never invokes those on an open generic type such as Singleton<T>. A stale instance
        // needs no reset because a destroyed component already compares equal to null.
        isQuitting = false;
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
