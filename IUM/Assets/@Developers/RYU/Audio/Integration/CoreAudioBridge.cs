using UnityEngine;
using CoreAudioManager = Core.Audio.AudioManager;

/// <summary>
/// 구 IUM <c>AudioManager</c>가 직접 들고 있던 두 가지를 <c>Core.Audio.AudioManager</c>에 이어 준다.
///
/// 1. 저장된 볼륨 적용 — 구 버전은 <c>DataManager.ApplyAudioSettings</c>가 직접 호출했다
/// 2. 씬 전환 시 채널 회수 — 구 버전은 <c>ISceneEventListener</c>를 직접 구현했다
///
/// Playground의 <c>AudioSceneBridge</c>는 그쪽 <c>ISceneTransitionListener</c>
/// (<c>OnSceneLeaving</c>/<c>OnSceneEntered</c>)를 구현하므로 IUM의
/// <c>ISceneEventListener</c>(<c>OnSceneLoadStart</c>/<c>OnSceneLoadComplete</c>)와 맞지 않아
/// 가져오지 않고 이쪽에서 다시 썼다.
///
/// <c>Core.Audio.AudioManager</c>는 지연 생성을 하지 않으므로 같은 씬에 컴포넌트가 배치되어 있어야 한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CoreAudioBridge : MonoBehaviour, ISceneEventListener
{
    [SerializeField] CoreAudioManager audioManager;

    bool _registered;

    void Awake()
    {
        if (audioManager == null) audioManager = FindAnyObjectByType<CoreAudioManager>();
    }

    void OnEnable()
    {
        if (DataManager.HasInstance)
        {
            var data = DataManager.Instance;
            data.Ready += Sync;
            if (data.IsReady) Sync();
        }

        var controller = SceneController.Instance;
        if (controller != null)
        {
            controller.RegisterListener(this);
            _registered = true;
        }
    }

    void OnDisable()
    {
        if (DataManager.TryGetInstance(out var data)) data.Ready -= Sync;

        if (_registered && SceneController.TryGetInstance(out var controller))
        {
            controller.UnregisterListener(this);
            _registered = false;
        }
    }

    /// <summary>
    /// 저장된 볼륨을 버스에 싣는다. 마스터는 저장 값이 남아 있지만 옵션 UI에서 제거됐다 (ISSUE-009).
    /// </summary>
    public void Sync()
    {
        if (audioManager == null || !DataManager.HasInstance) return;

        var settings = DataManager.Instance.Settings;
        if (settings == null) return;

        var mixer = audioManager.Mixer;
        mixer.MasterVolume = settings.MasterVolume;
        mixer.BgmVolume = settings.MusicVolume;
        mixer.SfxVolume = settings.EnvironmentVolume;
        mixer.DialogueVolume = settings.DialogueVolume;
        mixer.VideoVolume = settings.VideoVolume;
    }

    // 매 프레임 폴링하던 Update는 지웠다 (ISSUE-015). DataManager.ApplyAudioSettings가 이제
    // 이 버스를 직접 갱신하므로, 옵션 슬라이더 변경이 폴링 없이 곧바로 반영된다.

    public void OnSceneLoadStart(string sceneName)
    {
        if (audioManager != null) audioManager.StopSceneSounds();
    }

    public void OnSceneLoadComplete(string sceneName) { }
}
