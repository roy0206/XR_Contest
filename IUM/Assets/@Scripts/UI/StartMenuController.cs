using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 메인 화면 (F-001). 게임 시작 · 이어하기 · 옵션 · 나가기.
///
/// Routing is not decided here: <see cref="GameFlow"/> reads the saved progress and picks the
/// destination. This screen only asks the questions the document requires — overwriting an existing
/// save, and whether a save exists at all.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class StartMenuController : MonoBehaviour
{
    [Tooltip("외부(예: FixedUI 3D 모니터) 메뉴가 기본 버튼을 대신 표시할 때 사용합니다.")]
    [SerializeField] bool externalPrimaryMenu;

    UIDocument _document;
    VisualElement _root;
    VisualElement _screen;
    VisualElement _menuCard;
    VisualElement _ambientTop;
    VisualElement _ambientBottom;
    VisualElement _optionsPanel;
    VisualElement _confirmPanel;
    Label _saveNotice;

    Button _startButton;
    Button _continueButton;
    Button _optionsButton;
    Button _exitButton;
    Button _closeOptionsButton;
    Button _confirmNewGameButton;
    Button _cancelNewGameButton;

    VolumeOptionsPanel _volumes;
    bool _dataReady;
    bool _busy;
    bool _hiddenForCutscene;
    float _menuAlpha = 1f;
    float _menuFadeSeconds = DefaultFadeSeconds;

    /// <summary>Used until the cutscene table is loaded, and matches its authored default.</summary>
    const float DefaultFadeSeconds = 0.5f;

    /// <summary>외부 메뉴가 이어하기 버튼의 활성 상태를 표시할 때 사용한다.</summary>
    public bool CanContinue => _dataReady && !_busy && GameFlow.Instance.CanContinue;

    void Awake() => _document = GetComponent<UIDocument>();

    void OnEnable()
    {
        if (_document?.visualTreeAsset == null) return;

        var root = _document.rootVisualElement;
        _screen = root.Q<VisualElement>("screen");
        _menuCard = root.Q<VisualElement>(className: "menu-card");
        _ambientTop = root.Q<VisualElement>(className: "ambient--top");
        _ambientBottom = root.Q<VisualElement>(className: "ambient--bottom");
        _optionsPanel = root.Q<VisualElement>("options-panel");
        _confirmPanel = root.Q<VisualElement>("confirm-panel");
        _saveNotice = root.Q<Label>("save-notice");

        _startButton = root.Q<Button>("start-button");
        _continueButton = root.Q<Button>("continue-button");
        _optionsButton = root.Q<Button>("options-button");
        _exitButton = root.Q<Button>("exit-button");
        _closeOptionsButton = root.Q<Button>("close-options-button");
        _confirmNewGameButton = root.Q<Button>("confirm-new-game-button");
        _cancelNewGameButton = root.Q<Button>("cancel-new-game-button");

        if (_optionsPanel == null || _confirmPanel == null || _saveNotice == null ||
            _startButton == null || _continueButton == null || _optionsButton == null ||
            _exitButton == null || _closeOptionsButton == null ||
            _confirmNewGameButton == null || _cancelNewGameButton == null)
        {
            Debug.LogError("[StartMenu] Required UI elements are missing from StartMenu.uxml.");
            return;
        }

        _root = root;
        ApplyPrimaryMenuMode();

        // Resolved before the first frame: this screen comes back mid-transition after the ending,
        // and starting at full opacity would flash the menu over the blackout.
        _hiddenForCutscene = ShouldHideMenu();
        _menuAlpha = _hiddenForCutscene ? 0f : 1f;
        ApplyMenuAlpha();

        _volumes = new VolumeOptionsPanel(_optionsPanel);

        _startButton.clicked += OnStartClicked;
        _continueButton.clicked += OnContinueClicked;
        _optionsButton.clicked += ShowOptions;
        _exitButton.clicked += OnExitClicked;
        _closeOptionsButton.clicked += HideModals;
        _confirmNewGameButton.clicked += OnConfirmNewGame;
        _cancelNewGameButton.clicked += HideModals;

        HideModals();

        // Continue stays disabled until the save is known to exist; the rest of the menu works
        // immediately so the player is never left with a dead screen while data loads.
        _continueButton.SetEnabled(false);
        _ = InitializeDataAsync();
    }

    void OnDisable()
    {
        if (_startButton != null) _startButton.clicked -= OnStartClicked;
        if (_continueButton != null) _continueButton.clicked -= OnContinueClicked;
        if (_optionsButton != null) _optionsButton.clicked -= ShowOptions;
        if (_exitButton != null) _exitButton.clicked -= OnExitClicked;
        if (_closeOptionsButton != null) _closeOptionsButton.clicked -= HideModals;
        if (_confirmNewGameButton != null) _confirmNewGameButton.clicked -= OnConfirmNewGame;
        if (_cancelNewGameButton != null) _cancelNewGameButton.clicked -= HideModals;

        _volumes?.Dispose();
        _volumes = null;
        _root = null;
        _screen = null;
        _menuCard = null;
        _ambientTop = null;
        _ambientBottom = null;
    }

    void Update()
    {
        _volumes?.Tick();
        TickMenuVisibility();
    }

    /// <summary>
    /// 컷씬과 씬 전환 동안 메뉴를 물린다. 컷씬은 이 씬을 끄지 않고 위에 겹쳐 올리는 방식이라 메인
    /// 화면이 그대로 남는데, UIToolkit 패널은 <see cref="ScreenFader"/>의 캔버스 **위에** 그려져
    /// 암전으로도 가려지지 않는다. 그래서 스스로 물러나야 한다.
    ///
    /// 즉시 끄지 않고 컷씬의 진입 암전과 같은 시간에 걸쳐 알파를 내린다. 한 프레임에 사라지면 화면이
    /// 어두워지는 것과 무관하게 메뉴만 툭 없어져 끊겨 보인다.
    ///
    /// Polled rather than subscribed to <see cref="CutsceneDirector.Started"/>: the director may not
    /// exist yet when this enables (a Singleton is created on first access), and polling covers the
    /// abort path and scene transitions with no extra bookkeeping.
    /// </summary>
    void TickMenuVisibility()
    {
        if (_root == null) return;

        var hidden = ShouldHideMenu();
        if (hidden != _hiddenForCutscene)
        {
            _hiddenForCutscene = hidden;
            _menuFadeSeconds = ResolveFadeSeconds(hidden);
        }

        var target = hidden ? 0f : 1f;
        if (Mathf.Approximately(_menuAlpha, target)) return;

        // 일시정지 중에는 컷씬도 멈춰 있다. Carrying this fade on alone would drift out of step.
        if (PauseService.IsPaused) return;

        var step = _menuFadeSeconds > 0f ? Time.unscaledDeltaTime / _menuFadeSeconds : 1f;
        _menuAlpha = Mathf.MoveTowards(_menuAlpha, target, step);
        ApplyMenuAlpha();
    }

    /// <summary>
    /// 씬 전환도 포함한다. 전환은 페이더로 암전한 채 진행하는데, 이 메뉴는 그 암전 위에 그려지므로
    /// 그대로 두면 로딩 내내 떠 있다.
    /// </summary>
    static bool ShouldHideMenu() =>
        (CutsceneDirector.HasInstance && CutsceneDirector.Instance.IsPlaying) ||
        SceneController.IsTransitioning;

    static float ResolveFadeSeconds(bool hiding)
    {
        if (!CutsceneDirector.HasInstance) return DefaultFadeSeconds;

        var director = CutsceneDirector.Instance;
        var seconds = hiding ? director.EnterFadeSeconds : director.OutroFadeSeconds;
        return seconds > 0f ? seconds : DefaultFadeSeconds;
    }

    void ApplyMenuAlpha()
    {
        _root.style.opacity = _menuAlpha;

        // display, not visibility alone: at zero the menu also leaves layout and picking, so a
        // button cannot be clicked through the cutscene.
        _root.style.display = _menuAlpha > 0f ? DisplayStyle.Flex : DisplayStyle.None;
    }

    async Task InitializeDataAsync()
    {
        try
        {
            await DataManager.Instance.InitializeAsync();
            await GameFlow.Instance.InitializeAsync();
            if (this == null) return;

            _dataReady = true;
            _volumes?.Refresh();
            RefreshContinue();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[StartMenu] Data initialization failed: {exception.Message}");
            ShowNotice("저장 정보를 불러오지 못했습니다. 새 게임으로 시작해 주세요.");
        }
    }

    /// <summary>저장 데이터 유무에 따라 이어하기를 켜고 끈다 (F-001 1.5, 1.8).</summary>
    void RefreshContinue()
    {
        var canContinue = GameFlow.Instance.CanContinue;
        _continueButton.SetEnabled(canContinue);

        if (DataManager.Instance.UserDataCorrupted)
        {
            ShowNotice("저장 정보를 불러오지 못했습니다. 새 게임으로 시작해 주세요.");
            return;
        }

        if (!canContinue)
        {
            ShowNotice("저장된 진행 정보가 없습니다.");
            return;
        }

        ShowNotice(null);
    }

    void ShowNotice(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _saveNotice.RemoveFromClassList("save-notice--visible");
            _saveNotice.text = string.Empty;
            return;
        }

        _saveNotice.text = text;
        _saveNotice.AddToClassList("save-notice--visible");
    }

    /// <summary>FixedUI처럼 별도 화면이 기본 메뉴를 그릴 때 런타임에서 전환한다.</summary>
    public void UseExternalPrimaryMenu(bool value)
    {
        externalPrimaryMenu = value;
        ApplyPrimaryMenuMode();
    }

    void ApplyPrimaryMenuMode()
    {
        if (_screen == null) return;

        _screen.style.backgroundColor = externalPrimaryMenu
            ? new StyleColor(Color.clear)
            : StyleKeyword.Null;

        var display = externalPrimaryMenu ? DisplayStyle.None : DisplayStyle.Flex;
        if (_menuCard != null) _menuCard.style.display = display;
        if (_ambientTop != null) _ambientTop.style.display = display;
        if (_ambientBottom != null) _ambientBottom.style.display = display;
    }

    // FixedUI의 원본 uGUI 버튼은 아래의 공개 진입점만 호출한다. 저장/전환/옵션 처리는
    // 계속 이 컨트롤러 한 곳에 남겨 두어 두 메뉴 구현이 서로 다른 규칙을 갖지 않게 한다.
    public void RequestStartNewGame() => OnStartClicked();
    public void RequestContinue() => OnContinueClicked();
    public void RequestOptions() => ShowOptions();
    public void RequestExit() => OnExitClicked();

    /// <summary>
    /// 게임 시작 (F-001 1.4). 저장 데이터가 있으면 덮어쓰기 전에 확인부터 받는다.
    /// </summary>
    void OnStartClicked()
    {
        if (_busy) return;

        if (_dataReady && GameFlow.Instance.CanContinue)
        {
            ShowPanel(_confirmPanel);
            return;
        }

        _ = StartNewGameAsync();
    }

    void OnConfirmNewGame()
    {
        HideModals();
        _ = StartNewGameAsync();
    }

    async Task StartNewGameAsync()
    {
        if (_busy) return;
        SetBusy(true);

        try
        {
            await GameFlow.Instance.StartNewGameAsync();
        }
        finally
        {
            // Re-enabled because GameFlow leaves the player here when a destination is missing,
            // which is the normal state while the main content is unbuilt.
            SetBusy(false);
            RefreshContinue();
        }
    }

    void OnContinueClicked()
    {
        if (_busy || !_dataReady) return;
        _ = ContinueAsync();
    }

    async Task ContinueAsync()
    {
        SetBusy(true);

        try
        {
            await GameFlow.Instance.ContinueAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    void SetBusy(bool value)
    {
        _busy = value;
        _startButton.SetEnabled(!value);
        _optionsButton.SetEnabled(!value);
        _exitButton.SetEnabled(!value);
        _continueButton.SetEnabled(!value && _dataReady && GameFlow.Instance.CanContinue);
    }

    void ShowOptions()
    {
        ShowPanel(_optionsPanel);
        _volumes?.Refresh();
    }

    void HideModals() => ShowPanel(null);

    void ShowPanel(VisualElement panel)
    {
        _optionsPanel.style.display = panel == _optionsPanel ? DisplayStyle.Flex : DisplayStyle.None;
        _confirmPanel.style.display = panel == _confirmPanel ? DisplayStyle.Flex : DisplayStyle.None;
    }

    async void OnExitClicked()
    {
        // Flushes any pending volume write before the process goes away (F-002 2.5).
        _volumes?.Dispose();
        _volumes = null;

        await Task.Yield();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
