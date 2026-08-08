using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 자막 (F-018 1.4). Screen-overlay UIDocument so the whole dialogue flow is verifiable without an
/// HMD; the world-space VR subtitle can replace this view without touching the dialogue layer.
///
/// Serves both 인게임 대사 and 컷씬 대사. The two never overlap — a cutscene locks the inputs that
/// trigger in-game lines and holds the process gate — so one view can take both without arbitrating.
///
/// Deliberately independent of 대사 볼륨: a subtitle must show even when dialogue audio is muted.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class SubtitleView : MonoBehaviour
{
    UIDocument _document;
    VisualElement _box;
    Label _speakerLabel;
    Label _textLabel;
    InGameDialogue _dialogue;
    CutsceneDirector _cutscene;

    void Awake() => _document = GetComponent<UIDocument>();

    void OnEnable()
    {
        if (_document?.visualTreeAsset == null) return;

        var root = _document.rootVisualElement;
        _box = root.Q<VisualElement>("subtitle-box");
        _speakerLabel = root.Q<Label>("subtitle-speaker");
        _textLabel = root.Q<Label>("subtitle-text");

        if (_box == null || _speakerLabel == null || _textLabel == null)
        {
            Debug.LogError("[Subtitle] Required elements are missing from Subtitle.uxml.");
            return;
        }

        Hide();

        _dialogue = Find<InGameDialogue>();
        if (_dialogue != null) _dialogue.SubtitleChanged += OnSubtitleChanged;

        _cutscene = Find<CutsceneDirector>();
        if (_cutscene != null) _cutscene.SubtitleChanged += OnSubtitleChanged;
    }

    void OnDisable()
    {
        if (_dialogue != null)
        {
            _dialogue.SubtitleChanged -= OnSubtitleChanged;
            _dialogue = null;
        }

        if (_cutscene == null) return;
        _cutscene.SubtitleChanged -= OnSubtitleChanged;
        _cutscene = null;
    }

    /// <summary>
    /// Binds to a source only if it already exists. Singleton.Instance would create one, and a
    /// subtitle view has no business spinning up a dialogue or cutscene service that this scene
    /// never asked for. The scene lookup covers the case where the source's Awake has not run yet.
    /// </summary>
    static T Find<T>() where T : MonoBehaviour =>
        Singleton<T>.HasInstance
            ? Singleton<T>.Instance
            : FindAnyObjectByType<T>(FindObjectsInactive.Include);

    void OnSubtitleChanged(DialogueSpeaker speaker, string text)
    {
        if (_box == null) return;

        if (string.IsNullOrWhiteSpace(text))
        {
            Hide();
            return;
        }

        _speakerLabel.text = GetSpeakerName(speaker);
        _speakerLabel.style.display = string.IsNullOrEmpty(_speakerLabel.text)
            ? DisplayStyle.None
            : DisplayStyle.Flex;

        _textLabel.text = text;
        ApplySpeakerModifier(speaker);
        _box.AddToClassList("subtitle-box--visible");
    }

    void Hide()
    {
        _box.RemoveFromClassList("subtitle-box--visible");
        _textLabel.text = string.Empty;
        _speakerLabel.text = string.Empty;
    }

    void ApplySpeakerModifier(DialogueSpeaker speaker)
    {
        _speakerLabel.RemoveFromClassList("subtitle-speaker--nojang");
        _speakerLabel.RemoveFromClassList("subtitle-speaker--ieumi");

        var modifier = speaker switch
        {
            DialogueSpeaker.Nojang => "subtitle-speaker--nojang",
            DialogueSpeaker.Ieumi => "subtitle-speaker--ieumi",
            _ => null
        };

        if (modifier != null) _speakerLabel.AddToClassList(modifier);
    }

    /// <summary>Narration carries no name tag, so the line reads as voice-over.</summary>
    static string GetSpeakerName(DialogueSpeaker speaker) => speaker switch
    {
        DialogueSpeaker.Nojang => "노장",
        DialogueSpeaker.Ieumi => "이음이",
        _ => string.Empty
    };
}
