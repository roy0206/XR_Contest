using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// FixedUI.zip의 원본 World Space uGUI 버튼을 프로젝트의 기존 시작 흐름에 연결한다.
/// 시각 계층과 버튼의 영구 이벤트는 원본 씬 그대로 유지하고, 동작만 StartMenuController로 위임한다.
/// </summary>
public sealed class FixedUIStartMenuAdapter : MonoBehaviour
{
    [Header("원본 FixedUI 연결")]
    [SerializeField] GameObject menuRoot;
    [SerializeField] GameObject optionsPanel;
    [SerializeField] Button continueButton;

    [Header("원본 FixedUI 렌더링")]
    [SerializeField] RenderPipelineAsset fixedUIPipeline;
    [SerializeField] Font koreanFontSource;

    StartMenuController _startMenu;
    bool _missingControllerReported;
    RenderPipelineAsset _previousPipeline;
    TMP_FontAsset _runtimeKoreanFont;

    void Awake()
    {
        _previousPipeline = QualitySettings.renderPipeline;
        if (fixedUIPipeline != null && QualitySettings.renderPipeline != fixedUIPipeline)
            QualitySettings.renderPipeline = fixedUIPipeline;

        if (koreanFontSource != null && menuRoot != null)
        {
            _runtimeKoreanFont = TMP_FontAsset.CreateFontAsset(koreanFontSource);
            _runtimeKoreanFont.name = "FixedUI Korean Runtime Font";
            foreach (var text in menuRoot.GetComponentsInChildren<TMP_Text>(true))
                text.font = _runtimeKoreanFont;
        }

        ApplyStartScreenLabels();
    }

    void Start()
    {
        // 이 씬은 시작 화면이므로 패키지의 pauseTimeScale 설정을 따르지 않는다.
        Time.timeScale = 1f;
        if (menuRoot != null) menuRoot.SetActive(true);

        // 원본 씬은 World Space Canvas의 Event Camera가 비어 있어 포인터 입력 경고가 난다.
        // 화면 배치에는 손대지 않고, 현재 메인 카메라만 이벤트 카메라로 연결한다.
        var canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            canvas.worldCamera = Camera.main;

        ResolveStartMenu();
    }

    void Update()
    {
        if (_startMenu == null) ResolveStartMenu();
        if (continueButton != null)
            continueButton.interactable = _startMenu != null && _startMenu.CanContinue;
    }

    void ResolveStartMenu()
    {
        _startMenu = FindFirstObjectByType<StartMenuController>();
        if (_startMenu != null)
        {
            _startMenu.UseExternalPrimaryMenu(true);
            _missingControllerReported = false;
            return;
        }

        if (_missingControllerReported) return;
        _missingControllerReported = true;
        Debug.LogError("[FixedUI Start] StartMenuController를 찾을 수 없습니다.");
    }

    void ApplyStartScreenLabels()
    {
        SetButtonLabel("Button_Continue", "이어하기");
        SetButtonLabel("Button_Restart", "게임 시작");
        SetButtonLabel("Button_Main", "나가기");
    }

    void SetButtonLabel(string buttonName, string label)
    {
        if (menuRoot == null) return;

        var buttonTransform = menuRoot.transform.Find($"ButtonGroup/{buttonName}");
        var text = buttonTransform != null ? buttonTransform.GetComponentInChildren<TMP_Text>(true) : null;
        if (text != null) text.text = label;
    }

    // 메서드 이름은 원본 씬 Button.onClick 연결을 보존하기 위해 유지한다.
    public void OnContinue()
    {
        if (_startMenu == null) ResolveStartMenu();
        _startMenu?.RequestContinue();
    }

    public void OnOptions()
    {
        if (_startMenu == null) ResolveStartMenu();
        _startMenu?.RequestOptions();
    }

    public void OnOptionsBack()
    {
        if (optionsPanel != null) optionsPanel.SetActive(false);
    }

    public void OnRestart()
    {
        if (_startMenu == null) ResolveStartMenu();
        _startMenu?.RequestStartNewGame();
    }

    public void OnMainMenu()
    {
        if (_startMenu == null) ResolveStartMenu();
        _startMenu?.RequestExit();
    }

    void OnDestroy()
    {
        if (QualitySettings.renderPipeline == fixedUIPipeline)
            QualitySettings.renderPipeline = _previousPipeline;

        if (_runtimeKoreanFont != null)
            Destroy(_runtimeKoreanFont);
    }
}
