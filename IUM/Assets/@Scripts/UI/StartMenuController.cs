using UnityEngine;
using UnityEngine.UIElements;

/// <summary>UIDocument-backed start menu configured in StartScene.</summary>
[RequireComponent(typeof(UIDocument))]
public sealed class StartMenuController : MonoBehaviour
{
    [SerializeField] string targetSceneName = "SampleScene";
    [SerializeField, Min(0f)] float saveDebounceSeconds = 0.35f;

    UIDocument _document;
    Button _startButton;
    Button _optionsButton;
    Button _exitButton;
    Button _closeOptionsButton;
    VisualElement _optionsPanel;
    Slider _volumeSlider;
    bool _dataReady;
    bool _hasPendingInitialVolume;
    bool _volumeSavePending;
    float _pendingInitialVolume;
    float _saveAt;

    void Awake() => _document = GetComponent<UIDocument>();

    void OnEnable()
    {
        if (_document?.visualTreeAsset == null) return;

        var root = _document.rootVisualElement;
        _startButton = root.Q<Button>("start-button");
        _optionsButton = root.Q<Button>("options-button");
        _exitButton = root.Q<Button>("exit-button");
        _closeOptionsButton = root.Q<Button>("close-options-button");
        _optionsPanel = root.Q<VisualElement>("options-panel");
        _volumeSlider = root.Q<Slider>("volume-slider");

        if (_startButton == null || _optionsButton == null || _exitButton == null ||
            _closeOptionsButton == null || _optionsPanel == null || _volumeSlider == null)
        {
            Debug.LogError("[StartMenu] Required UI elements are missing from StartMenu.uxml.");
            return;
        }

        _startButton.clicked += StartGame;
        _optionsButton.clicked += ToggleOptions;
        _exitButton.clicked += ExitGame;
        _closeOptionsButton.clicked += HideOptions;
        _volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);

        // Menu interaction and previewing volume do not depend on saved data.
        SetButtonsEnabled(true);
        _volumeSlider.SetEnabled(true);
        HideOptions();
        _ = InitializeDataAsync();
    }

    void OnDisable()
    {
        if (_startButton != null) _startButton.clicked -= StartGame;
        if (_optionsButton != null) _optionsButton.clicked -= ToggleOptions;
        if (_exitButton != null) _exitButton.clicked -= ExitGame;
        if (_closeOptionsButton != null) _closeOptionsButton.clicked -= HideOptions;
        if (_volumeSlider != null) _volumeSlider.UnregisterValueChangedCallback(OnVolumeChanged);
        if (_volumeSavePending && DataManager.HasInstance && DataManager.Instance.IsReady)
            _ = FlushVolumeAsync();
    }

    void Update()
    {
        if (_volumeSavePending && Time.unscaledTime >= _saveAt)
            _ = FlushVolumeAsync();
    }

    async System.Threading.Tasks.Task InitializeDataAsync()
    {
        try
        {
            await DataManager.Instance.InitializeAsync();
            if (this == null) return;

            _dataReady = true;

            if (_hasPendingInitialVolume)
            {
                DataManager.Instance.Settings.MasterVolume = _pendingInitialVolume;
                _volumeSavePending = true;
                _saveAt = Time.unscaledTime + saveDebounceSeconds;
                _hasPendingInitialVolume = false;
            }
            else
            {
                var savedVolume = DataManager.Instance.Settings.MasterVolume;
                _volumeSlider.SetValueWithoutNotify(savedVolume);
                AudioManager.Instance.MasterVolume = savedVolume;
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[StartMenu] Data initialization failed: {exception.Message}");
        }
    }

    void StartGame()
    {
        if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            Debug.LogError($"[StartMenu] '{targetSceneName}' is not available in Build Settings.");
            return;
        }

        SetButtonsEnabled(false);
        SceneController.Instance.LoadScene(targetSceneName);
    }

    void ToggleOptions()
    {
        var isVisible = _optionsPanel.resolvedStyle.display != DisplayStyle.None;
        _optionsPanel.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
    }

    void HideOptions() => _optionsPanel.style.display = DisplayStyle.None;

    void OnVolumeChanged(ChangeEvent<float> evt)
    {
        var volume = Mathf.Clamp01(evt.newValue);
        AudioManager.Instance.MasterVolume = volume;

        if (!_dataReady)
        {
            _pendingInitialVolume = volume;
            _hasPendingInitialVolume = true;
            return;
        }

        DataManager.Instance.Settings.MasterVolume = volume;
        _volumeSavePending = true;
        _saveAt = Time.unscaledTime + saveDebounceSeconds;
    }

    async System.Threading.Tasks.Task FlushVolumeAsync()
    {
        if (!_volumeSavePending || !DataManager.HasInstance || !DataManager.Instance.IsReady) return;
        _volumeSavePending = false;
        try
        {
            await DataManager.Instance.SaveUserAsync();
        }
        catch (System.Exception exception)
        {
            _volumeSavePending = true;
            _saveAt = Time.unscaledTime + 1f;
            Debug.LogError($"[StartMenu] Failed to save volume: {exception.Message}");
        }
    }

    void SetButtonsEnabled(bool enabledState)
    {
        _startButton?.SetEnabled(enabledState);
        _optionsButton?.SetEnabled(enabledState);
        _exitButton?.SetEnabled(enabledState);
    }

    async void ExitGame()
    {
        if (_volumeSavePending)
            await FlushVolumeAsync();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
