using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 컷씬 화면 요소: 전체화면 캡션과 건너뛰기 게이지. 화자 자막은 <see cref="SubtitleView"/>가 그대로
/// 맡고, 배경·이미지·연출은 컷씬 씬이 직접 그린다.
///
/// Lives outside the cutscene scene because both elements have to survive the overlay being loaded
/// and unloaded — the 건너뛰기 게이지 in particular must be up before the stage exists.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class CutsceneView : MonoBehaviour
{
    UIDocument _document;
    VisualElement _captionBox;
    Label _caption;
    VisualElement _skip;
    VisualElement _skipFill;
    CutsceneDirector _director;

    void Awake() => _document = GetComponent<UIDocument>();

    void OnEnable()
    {
        if (_document?.visualTreeAsset == null) return;

        var root = _document.rootVisualElement;
        _captionBox = root.Q<VisualElement>("cutscene-caption-box");
        _caption = root.Q<Label>("cutscene-caption");
        _skip = root.Q<VisualElement>("cutscene-skip");
        _skipFill = root.Q<VisualElement>("cutscene-skip-fill");

        if (_captionBox == null || _caption == null || _skip == null || _skipFill == null)
        {
            Debug.LogError("[Cutscene] Required elements are missing from Cutscene.uxml.");
            return;
        }

        SetCaption(string.Empty);
        SetSkipProgress(0f);

        // Bound only if a director already exists: Singleton.Instance would create one, and a view
        // has no business spinning up a service this scene never asked for.
        _director = CutsceneDirector.HasInstance
            ? CutsceneDirector.Instance
            : FindAnyObjectByType<CutsceneDirector>(FindObjectsInactive.Include);
        if (_director == null) return;

        _director.CaptionChanged += SetCaption;
        _director.SkipProgressChanged += SetSkipProgress;
    }

    void OnDisable()
    {
        if (_director == null) return;

        _director.CaptionChanged -= SetCaption;
        _director.SkipProgressChanged -= SetSkipProgress;
        _director = null;
    }

    void SetCaption(string text)
    {
        if (_captionBox == null) return;

        if (string.IsNullOrWhiteSpace(text))
        {
            _captionBox.RemoveFromClassList("cutscene-caption-box--visible");
            _caption.text = string.Empty;
            return;
        }

        _caption.text = text;
        _captionBox.AddToClassList("cutscene-caption-box--visible");
    }

    /// <summary>Zero hides the gauge, so it only appears once the player starts holding.</summary>
    void SetSkipProgress(float progress)
    {
        if (_skip == null) return;

        if (progress <= 0f)
        {
            _skip.RemoveFromClassList("cutscene-skip--visible");
            _skipFill.style.width = new StyleLength(Length.Percent(0f));
            return;
        }

        _skip.AddToClassList("cutscene-skip--visible");
        _skipFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(progress) * 100f));
    }
}
