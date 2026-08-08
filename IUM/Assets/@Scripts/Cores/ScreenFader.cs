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
        // Also rebinds when the bound camera is merely disabled, not destroyed. A cutscene overlay
        // switches the main camera off for its own, and a disabled camera is not null — leaving the
        // canvas pointed at it renders nothing at all.
        if (_canvas != null && (_canvas.worldCamera == null || !_canvas.worldCamera.isActiveAndEnabled))
            BindCamera();
    }

    /// <summary>
    /// Re-resolves the camera immediately. Call right after enabling or disabling a camera so the
    /// fade does not blink out for the one frame before <see cref="LateUpdate"/> notices.
    /// </summary>
    public void Rebind()
    {
        EnsureOverlay();
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
        var camera = ResolveCamera();
        if (camera == null) return;
        _canvas.worldCamera = camera;
        _canvas.planeDistance = Mathf.Max(camera.nearClipPlane + cameraPlaneOffset, 0.1f);
    }

    /// <summary>
    /// Camera.main only ever returns an enabled camera tagged MainCamera, so it goes null while a
    /// cutscene has the main camera switched off for its own untagged one. Falling back to whichever
    /// enabled camera draws last keeps the fade attached to what the player is actually seeing.
    /// </summary>
    static Camera ResolveCamera()
    {
        var main = Camera.main;
        if (main != null) return main;

        // Camera.allCameras lists enabled cameras only.
        var cameras = Camera.allCameras;
        Camera last = null;
        for (var i = 0; i < cameras.Length; i++)
            if (last == null || cameras[i].depth > last.depth)
                last = cameras[i];

        return last;
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
