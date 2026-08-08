using UnityEngine;

namespace Core.Foundation
{
    /// <summary>
    /// Playground `Core.Foundation`에서 그대로 가져왔다. IUM 전역 <c>Singleton&lt;T&gt;</c>와 이름이
    /// 같으므로 네임스페이스로 격리한다. 두 구현은 계약이 다르다.
    ///
    /// - IUM 전역: <c>where T : MonoBehaviour</c>, 인스턴스가 없으면 <c>Instance</c>가 만들어 준다
    /// - 이쪽: <c>where T : Singleton&lt;T&gt;</c>, 지연 생성이 없고 씬에 배치되어 있어야 한다
    ///
    /// 이 타입을 상속할 때는 <b>반드시 <c>Core.Foundation.Singleton&lt;T&gt;</c>로 완전 수식</b>한다.
    /// 파일 상단 <c>using Core.Foundation;</c>만 두고 무자격으로 <c>Singleton&lt;T&gt;</c>라고 쓰면
    /// 전역 네임스페이스 멤버가 using 지시문보다 우선하므로 IUM 쪽 구현이 조용히 선택된다.
    /// IUM 쪽 제약이 <c>MonoBehaviour</c>라 컴파일까지 통과해 버려 발견이 늦다.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
    {
        private static T instance;
        private static bool applicationIsQuitting;
        private static bool missingInstanceWasReported;

        public static T Instance
        {
            get
            {
                if (applicationIsQuitting)
                    return null;

                if (instance == null && !missingInstanceWasReported)
                {
                    missingInstanceWasReported = true;
                    Debug.LogError(
                        $"[{typeof(T).Name}] No singleton instance is registered. " +
                        "Place one in the startup scene or a bootstrap scene before using it.");
                }

                return instance;
            }
        }

        public static bool HasInstance => !applicationIsQuitting && instance != null;

        public static bool TryGetInstance(out T value)
        {
            value = applicationIsQuitting ? null : instance;
            return value != null;
        }

        private void Awake()
        {
            applicationIsQuitting = false;
            missingInstanceWasReported = false;

            T self = (T)this;
            if (instance != null && instance != self)
            {
                Debug.LogWarning(
                    $"[{typeof(T).Name}] A duplicate singleton component was removed.",
                    this);

                enabled = false;
                Destroy(this);
                return;
            }

            instance = self;

            if (transform.parent != null)
                transform.SetParent(null, true);

            DontDestroyOnLoad(gameObject);
            OnRegistered();
        }

        private void OnDestroy()
        {
            T self = (T)this;
            if (instance != self)
                return;

            OnUnregistering();
            instance = null;
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        protected virtual void OnRegistered()
        {
        }

        protected virtual void OnUnregistering()
        {
        }
    }
}
