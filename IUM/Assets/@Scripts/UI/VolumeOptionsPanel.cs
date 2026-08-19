using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 옵션의 볼륨 조정부 (F-002). Binds four sliders to <see cref="UserSettingsData"/> and writes the
/// file back on a debounce.
///
/// A plain class rather than a MonoBehaviour so both the 일시정지 메뉴 and the 메인 화면 옵션 can
/// drive the same behaviour from their own UXML — the caller supplies the root and pumps
/// <see cref="Tick"/>.
///
/// 슬라이더는 문서대로 0~100을 보여주고 저장은 0~1로 한다 (F-002 2.2, DataSystem 4.1).
/// </summary>
public sealed class VolumeOptionsPanel
{
    const float DisplayScale = 100f;

    readonly Slider _music;
    readonly Slider _dialogue;
    readonly Slider _environment;
    readonly Slider _video;
    readonly float _debounceSeconds;

    bool _savePending;
    float _saveAt;
    bool _suppressCallbacks;

    public bool IsBound =>
        _music != null && _dialogue != null && _environment != null && _video != null;

    /// <param name="root">Element containing the four sliders.</param>
    public VolumeOptionsPanel(VisualElement root, float debounceSeconds = 0.35f)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        _debounceSeconds = Mathf.Max(0f, debounceSeconds);
        _music = root.Q<Slider>("music-volume-slider");
        _dialogue = root.Q<Slider>("dialogue-volume-slider");
        _environment = root.Q<Slider>("environment-volume-slider");
        _video = root.Q<Slider>("video-volume-slider");

        if (!IsBound)
        {
            Debug.LogError("[Options] 볼륨 슬라이더를 찾을 수 없습니다. UXML의 요소 이름을 확인하십시오.");
            return;
        }

        _music.RegisterValueChangedCallback(OnMusicChanged);
        _dialogue.RegisterValueChangedCallback(OnDialogueChanged);
        _environment.RegisterValueChangedCallback(OnEnvironmentChanged);
        _video.RegisterValueChangedCallback(OnVideoChanged);
    }

    /// <summary>Pulls saved values into the sliders. Call whenever the panel becomes visible.</summary>
    public void Refresh()
    {
        if (!IsBound || !IsDataReady) return;

        var settings = DataManager.Instance.Settings;

        // Without notify: writing a slider would otherwise be read back as a player edit and
        // schedule a pointless save.
        _suppressCallbacks = true;
        _music.SetValueWithoutNotify(settings.MusicVolume * DisplayScale);
        _dialogue.SetValueWithoutNotify(settings.DialogueVolume * DisplayScale);
        _environment.SetValueWithoutNotify(settings.EnvironmentVolume * DisplayScale);
        _video.SetValueWithoutNotify(settings.VideoVolume * DisplayScale);
        _suppressCallbacks = false;
    }

    /// <summary>Runs the debounce. Unscaled so it still works while the game is paused.</summary>
    public void Tick()
    {
        if (!_savePending || Time.unscaledTime < _saveAt) return;
        _ = FlushAsync();
    }

    public void Dispose()
    {
        if (!IsBound) return;

        _music.UnregisterValueChangedCallback(OnMusicChanged);
        _dialogue.UnregisterValueChangedCallback(OnDialogueChanged);
        _environment.UnregisterValueChangedCallback(OnEnvironmentChanged);
        _video.UnregisterValueChangedCallback(OnVideoChanged);

        // 옵션 변경은 즉시 저장한다 (F-002 2.5). Closing the panel must not drop a pending write.
        if (_savePending) _ = FlushAsync();
    }

    static bool IsDataReady => DataManager.HasInstance && DataManager.Instance.IsReady;

    void OnMusicChanged(ChangeEvent<float> evt) =>
        Apply(settings => settings.MusicVolume = ToNormalized(evt.newValue));

    void OnDialogueChanged(ChangeEvent<float> evt) =>
        Apply(settings => settings.DialogueVolume = ToNormalized(evt.newValue));

    void OnEnvironmentChanged(ChangeEvent<float> evt) =>
        Apply(settings => settings.EnvironmentVolume = ToNormalized(evt.newValue));

    void OnVideoChanged(ChangeEvent<float> evt) =>
        Apply(settings => settings.VideoVolume = ToNormalized(evt.newValue));

    void Apply(Action<UserSettingsData> mutate)
    {
        if (_suppressCallbacks) return;

        if (!IsDataReady)
        {
            // The panel stays usable before data loads, but there is nothing to write into yet.
            Debug.LogWarning("[Options] 저장 데이터가 아직 준비되지 않아 볼륨 변경을 반영하지 못했습니다.");
            return;
        }

        mutate(DataManager.Instance.Settings);

        // 변경된 소리를 즉시 미리 듣기 (F-002 2.3): music and environment reach the mixer here.
        // 대사 볼륨은 아직 믹서 채널이 없어 DialoguePlayer가 다음 줄에서 읽는다 (ISSUE-002).
        // 영상 볼륨도 같은 사정이라 CutsceneVideoSurface가 재생 중 매 프레임 다시 읽는다.
        DataManager.Instance.ApplyAudioSettings();

        _savePending = true;
        _saveAt = Time.unscaledTime + _debounceSeconds;
    }

    static float ToNormalized(float displayValue) => Mathf.Clamp01(displayValue / DisplayScale);

    async Task FlushAsync()
    {
        if (!_savePending || !IsDataReady) return;
        _savePending = false;

        try
        {
            await DataManager.Instance.SaveUserAsync();
        }
        catch (Exception exception)
        {
            // Retried rather than dropped: the player's setting is already applied in memory, so
            // losing the write would only show up as a reverted option next launch.
            _savePending = true;
            _saveAt = Time.unscaledTime + 1f;
            Debug.LogError($"[Options] 볼륨 저장 실패: {exception.Message}");
        }
    }
}
