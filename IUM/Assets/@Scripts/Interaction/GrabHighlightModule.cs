using UnityEngine;

/// <summary>
/// Hover feedback for a grabbable (F-005 1.5). Brightens the base colour through a
/// MaterialPropertyBlock, so it needs no outline shader and creates no material instances.
/// </summary>
public sealed class GrabHighlightModule : Module
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    readonly Renderer[] _renderers;
    readonly int[] _propertyIds;
    readonly Color[] _restColors;
    readonly MaterialPropertyBlock _block = new();

    public GrabHighlightModule(MonoThing thing) : base(thing)
    {
        _renderers = thing.GetComponentsInChildren<Renderer>(true);
        _propertyIds = new int[_renderers.Length];
        _restColors = new Color[_renderers.Length];

        for (var i = 0; i < _renderers.Length; i++)
        {
            var material = _renderers[i].sharedMaterial;
            if (material == null)
            {
                _propertyIds[i] = 0;
                continue;
            }

            // URP Lit exposes _BaseColor; older or custom shaders may only have _Color.
            if (material.HasProperty(BaseColorId)) _propertyIds[i] = BaseColorId;
            else if (material.HasProperty(ColorId)) _propertyIds[i] = ColorId;
            else continue;

            _restColors[i] = material.GetColor(_propertyIds[i]);
        }
    }

    public bool IsHighlighted { get; private set; }

    /// <summary>
    /// Tint blended in while highlighted. A tint is used instead of a brightness multiplier
    /// because multiplying does nothing to an already white material.
    /// </summary>
    public Color HighlightColor { get; set; } = new(1f, 0.82f, 0.35f, 1f);

    /// <summary>How far the rest colour is pushed towards <see cref="HighlightColor"/>.</summary>
    public float Blend { get; set; } = 0.65f;

    public void SetHighlighted(bool value)
    {
        if (IsHighlighted == value) return;
        IsHighlighted = value;
        Apply();
    }

    public override void OnRemoved() => SetHighlighted(false);

    void Apply()
    {
        for (var i = 0; i < _renderers.Length; i++)
        {
            var renderer = _renderers[i];
            if (renderer == null || _propertyIds[i] == 0) continue;

            var rest = _restColors[i];
            var color = rest;
            if (IsHighlighted)
            {
                color = Color.Lerp(rest, HighlightColor, Mathf.Clamp01(Blend));
                color.a = rest.a;
            }

            renderer.GetPropertyBlock(_block);
            _block.SetColor(_propertyIds[i], color);
            renderer.SetPropertyBlock(_block);
        }
    }
}
