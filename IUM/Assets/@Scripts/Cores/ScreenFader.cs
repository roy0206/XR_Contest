using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stereo-safe camera-space comfort fader. Screen Space Overlay is intentionally
/// avoided because it is not a reliable XR render target.
/// </summary>
public class ScreenFader : Singleton<ScreenFader>
{
    [SerializeField] Color color = Color.black;
    [SerializeField] int sortingOrder = 5000;
    [SerializeField, Min(0.01f)] float cameraPlaneOffset = 0.05f;

    Canvas _canvas;
    CanvasGroup _group;
    Image _image;
    Tween _current;

    public float Alpha => _group != null ? _group.alpha : 0f;

    protected override void Awake()
    {
        base.Awake();
        if (ReferenceEquals(Instance, this)) EnsureOverlay();
    }

    void LateUpdate()
    {
        if (_canvas != null && _canvas.worldCamera == null)
            BindCamera();
    }

    protected override void OnDestroy()
    {
        _current?.Kill();
        base.OnDestroy();
    }

    void EnsureOverlay()
    {
        if (_group != null)
        {
            BindCamera();
            return;
        }

        _canvas = GetComponent<Canvas>();
        if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.sortingOrder = sortingOrder;

        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        var overlay = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlay.transform.SetParent(transform, false);
        _image = overlay.GetComponent<Image>();
        _image.raycastTarget = false;
        _image.color = new Color(color.r, color.g, color.b, 1f);

        var rect = _image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        BindCamera();
    }

    void BindCamera()
    {
        if (_canvas == null) return;
        var camera = Camera.main;
        if (camera == null) return;
        _canvas.worldCamera = camera;
        _canvas.planeDistance = Mathf.Max(camera.nearClipPlane + cameraPlaneOffset, 0.1f);
    }

    public Tween FadeOut(float duration, Action onComplete = null) => FadeTo(1f, duration, onComplete);
    public Tween FadeIn(float duration, Action onComplete = null) => FadeTo(0f, duration, onComplete);

    public Tween FadeTo(float targetAlpha, float duration, Action onComplete = null)
    {
        EnsureOverlay();
        _current?.Kill();

        targetAlpha = Mathf.Clamp01(targetAlpha);
        _group.blocksRaycasts = targetAlpha > 0.001f;
        _current = DOTween
            .To(() => _group.alpha, value => _group.alpha = value, targetAlpha, Mathf.Max(0f, duration))
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

        if (onComplete != null) _current.OnComplete(() => onComplete());
        return _current;
    }

    public Tween Fade(bool fadeIn, Color fadeColor, float duration)
    {
        SetColor(fadeColor);
        return FadeTo(fadeIn ? 0f : 1f, duration);
    }

    public void SetColor(Color value)
    {
        EnsureOverlay();
        color = value;
        _image.color = new Color(value.r, value.g, value.b, 1f);
    }

    public void SetInstant(float alpha)
    {
        EnsureOverlay();
        _current?.Kill();
        _group.alpha = Mathf.Clamp01(alpha);
        _group.blocksRaycasts = _group.alpha > 0.001f;
    }
}
