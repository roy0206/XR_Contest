using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 일시정지 메뉴 화면 (F-019 2.2). Draws the menu, the volume options and the 메인 화면 확인 창;
/// every decision behind a button belongs to <see cref="PauseController"/>.
///
/// Screen-overlay so the whole menu is reachable without an HMD. The world-space VR menu can
/// replace this view without touching the pause layer.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class PauseMenuView : MonoBehaviour
{
    const float BindRetrySeconds = 0.5f;

    UIDocument _document;
    VisualElement _root;
    VisualElement _menuPanel;
    VisualElement _optionsPanel;
    VisualElement _confirmPanel;

    Button _resumeButton;
    Button _optionsButton;
    Button _restartButton;
    Button _mainMenuButton;
    Button _closeOptionsButton;
    Button _confirmYesButton;
    Button _confirmNoButton;

    VolumeOptionsPanel _volumes;
    PauseController _controller;
    float _nextBindAttempt;
    bool _viewReady;
    bool _missingElementsReported;

    void Awake() => _document = GetComponent<UIDocument>();

    void OnEnable()
    {
        _nextBindAttempt = 0f;
        if (!TryPrepareView()) return;
        TryBindController();
    }

    bool TryPrepareView()
    {
        if (_viewReady) return true;
        if (_document == null) _document = GetComponent<UIDocument>();
        if (_document?.visualTreeAsset == null) return false;

        var root = _document.rootVisualElement;
        _root = root.Q<VisualElement>("pause-root");
        _menuPanel = root.Q<VisualElement>("pause-menu-panel");
        _optionsPanel = root.Q<VisualElement>("pause-options-panel");
        _confirmPanel = root.Q<VisualElement>("pause-confirm-panel");

        _resumeButton = root.Q<Button>("resume-button");
        _optionsButton = root.Q<Button>("pause-options-button");
        _restartButton = root.Q<Button>("restart-process-button");
        _mainMenuButton = root.Q<Button>("main-menu-button");
        _closeOptionsButton = root.Q<Button>("close-options-button");
        _confirmYesButton = root.Q<Button>("confirm-yes-button");
        _confirmNoButton = root.Q<Button>("confirm-no-button");

        if (_root == null || _menuPanel == null || _optionsPanel == null || _confirmPanel == null ||
            _resumeButton == null || _optionsButton == null || _restartButton == null ||
            _mainMenuButton == null || _closeOptionsButton == null ||
            _confirmYesButton == null || _confirmNoButton == null)
        {
            if (!_missingElementsReported)
            {
                _missingElementsReported = true;
                Debug.LogError("[Pause] Required elements are missing from PauseMenu.uxml.");
            }
            return false;
        }

        _volumes = new VolumeOptionsPanel(_optionsPanel);

        _resumeButton.clicked += OnResume;
        _optionsButton.clicked += ShowOptions;
        _restartButton.clicked += OnRestartProcess;
        _mainMenuButton.clicked += ShowConfirm;
        _closeOptionsButton.clicked += ShowMenu;
        _confirmYesButton.clicked += OnConfirmMainMenu;
        _confirmNoButton.clicked += ShowMenu;

        SetVisible(false);
        _viewReady = true;
        return true;
    }

    bool TryBindController()
    {
        if (!_viewReady || _controller != null) return _controller != null;
        _controller = PauseController.HasInstance
            ? PauseController.Instance
            : FindAnyObjectByType<PauseController>(FindObjectsInactive.Include);
        if (_controller == null) return false;

        _controller.VisibilityChanged += SetVisible;
        if (_controller.IsOpen) SetVisible(true);
        return true;
    }

    void OnDisable()
    {
        if (_controller != null)
        {
            _controller.VisibilityChanged -= SetVisible;
            _controller = null;
        }

        if (_resumeButton != null) _resumeButton.clicked -= OnResume;
        if (_optionsButton != null) _optionsButton.clicked -= ShowOptions;
        if (_restartButton != null) _restartButton.clicked -= OnRestartProcess;
        if (_mainMenuButton != null) _mainMenuButton.clicked -= ShowConfirm;
        if (_closeOptionsButton != null) _closeOptionsButton.clicked -= ShowMenu;
        if (_confirmYesButton != null) _confirmYesButton.clicked -= OnConfirmMainMenu;
        if (_confirmNoButton != null) _confirmNoButton.clicked -= ShowMenu;

        _volumes?.Dispose();
        _volumes = null;
        _viewReady = false;
    }

    void Update()
    {
        if (!_viewReady && !TryPrepareView()) return;

        if (_controller == null && Time.unscaledTime >= _nextBindAttempt)
        {
            _controller = null;
            _nextBindAttempt = Time.unscaledTime + BindRetrySeconds;
            TryBindController();
        }

        _volumes?.Tick();
    }

    void SetVisible(bool visible)
    {
        if (_root == null) return;

        _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // Always reopens on the menu itself rather than wherever it was left.
        if (visible) ShowMenu();
    }

    void ShowMenu() => ShowPanel(_menuPanel);

    void ShowOptions()
    {
        ShowPanel(_optionsPanel);
        _volumes?.Refresh();
    }

    /// <summary>메인 화면 이동 확인 (F-019 2.4). 문구는 문서 원문을 그대로 쓴다.</summary>
    void ShowConfirm() => ShowPanel(_confirmPanel);

    void ShowPanel(VisualElement panel)
    {
        _menuPanel.style.display = panel == _menuPanel ? DisplayStyle.Flex : DisplayStyle.None;
        _optionsPanel.style.display = panel == _optionsPanel ? DisplayStyle.Flex : DisplayStyle.None;
        _confirmPanel.style.display = panel == _confirmPanel ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void OnResume() => _controller?.Close();
    void OnRestartProcess() => _controller?.RequestProcessRestart();
    void OnConfirmMainMenu() => _controller?.ReturnToMainMenu();
}
