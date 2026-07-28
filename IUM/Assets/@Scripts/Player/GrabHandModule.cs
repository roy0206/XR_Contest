using UnityEngine;

/// <summary>
/// One hand. Finds a nearby <see cref="Grabbable"/>, highlights it, and attaches or releases it.
/// Two instances live on the player so both hands can hold a tool at once (F-005 1.6).
/// </summary>
public sealed class GrabHandModule : Module
{
    const int MaxCandidates = 16;

    readonly Player _player;
    readonly XRHandSide _hand;
    readonly Collider[] _candidates = new Collider[MaxCandidates];

    Grabbable _hovered;
    float _trackingLostAt = -1f;

    public GrabHandModule(Player player, XRHandSide hand) : base(player)
    {
        _player = player;
        _hand = hand;
    }

    public XRHandSide Hand => _hand;
    public Transform Anchor => _player.GetHandAnchor(_hand);
    public Grabbable Held { get; private set; }
    public Grabbable Hovered => _hovered;

    public override void OnUpdate()
    {
        var anchor = Anchor;
        if (anchor == null) return;
        if (!UpdateTracking()) return;

        var phase = _player.Input.Commands.GetGrab(_hand);

        if (Held != null)
        {
            // Grip is hold-to-keep, so anything other than an active press drops the object.
            if (phase is GrabPhase.Released or GrabPhase.None) Release();
            return;
        }

        UpdateHover(anchor);
        if (phase == GrabPhase.Pressed && _hovered != null) Grab(_hovered);
    }

    public override void OnRemoved()
    {
        Release();
        SetHovered(null);
    }

    public void Release()
    {
        if (Held == null) return;

        var released = Held;
        Held = null;
        released.Detach(this);
    }

    /// <summary>
    /// F-005 1.7: a held object keeps its last valid pose during a short tracking dropout and is
    /// only dropped once the grace period expires.
    /// </summary>
    bool UpdateTracking()
    {
        if (_player.Input.IsHandTracked(_hand))
        {
            _trackingLostAt = -1f;
            return true;
        }

        if (Held == null)
        {
            SetHovered(null);
            return false;
        }

        if (_trackingLostAt < 0f) _trackingLostAt = Time.time;
        if (Time.time - _trackingLostAt < _player.TrackingLossGrace) return false;

        _trackingLostAt = -1f;
        Release();
        return false;
    }

    void UpdateHover(Transform anchor) => SetHovered(FindClosest(anchor));

    Grabbable FindClosest(Transform anchor)
    {
        var count = Physics.OverlapSphereNonAlloc(
            anchor.position, _player.GrabRadius, _candidates, _player.GrabLayers, QueryTriggerInteraction.Ignore);

        Grabbable closest = null;
        var closestDistance = float.MaxValue;

        for (var i = 0; i < count; i++)
        {
            var collider = _candidates[i];
            if (collider == null) continue;

            var candidate = collider.GetComponentInParent<Grabbable>();
            if (candidate == null || !candidate.CanGrab) continue;

            // Measured against the surface, not the origin, so a long member can be taken
            // anywhere along its body instead of only near its pivot.
            var distance = Vector3.Distance(anchor.position, collider.ClosestPoint(anchor.position));
            if (distance >= closestDistance || !candidate.IsWithinGrabDistance(distance)) continue;

            closest = candidate;
            closestDistance = distance;
        }

        return closest;
    }

    void SetHovered(Grabbable target)
    {
        if (ReferenceEquals(_hovered, target)) return;

        if (_hovered != null) _hovered.SetHighlighted(false);
        _hovered = target;
        if (_hovered != null) _hovered.SetHighlighted(true);
    }

    void Grab(Grabbable target)
    {
        if (!target.Attach(this, Anchor)) return;

        Held = target;
        SetHovered(null);
        UserInput.Instance.SendHapticImpulse(_hand, 0.3f, 0.05f);
    }
}
