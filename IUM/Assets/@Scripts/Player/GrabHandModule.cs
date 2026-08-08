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
    bool _isDistanceGrab;
    int _lockedAxis = 0; // 0 = None, 1 = Rotate, 2 = PushPull

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
            UpdateLaser(anchor); // Turn off laser when holding
            UpdatePushPullAndRotate();

            if (_player.Input.Commands.GetInteract(_hand))
            {
                Held.Activate();
            }

            // Grip is hold-to-keep, so anything other than an active press drops the object.
            if (phase is GrabPhase.Released or GrabPhase.None) Release();
            return;
        }

        UpdateHover(anchor);
        UpdateLaser(anchor);

        if (phase == GrabPhase.Pressed && _hovered != null) Grab(_hovered, _isDistanceGrab);
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
        // 1. 구체 범위 탐색 (근거리 잡기)
        _isDistanceGrab = false;
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

        // 2. 근거리(구체) 안에 잡을 물체가 없으면, 전방으로 레이저(Raycast)를 쏴서 원거리 잡기 탐색
        if (closest == null)
        {
            Vector3 rayOrigin = anchor.position;
            Vector3 rayDir = anchor.forward;

            if (_player.Input.Commands.IsDesktop)
            {
                rayOrigin = _player.Head.position;
                rayDir = _player.Head.forward;
            }
            else
            {
                rayDir = anchor.rotation * Quaternion.Euler(40f, 0f, 0f) * Vector3.forward;
            }

            if (RaycastGrabbable(rayOrigin, rayDir, out var hit, out var candidate))
            {
                closest = candidate;
                _isDistanceGrab = true;
            }
        }

        return closest;
    }

    public Quaternion GetRayRotation()
    {
        if (_player.Input.Commands.IsDesktop)
            return _player.Head.rotation;
        return Anchor.rotation * Quaternion.Euler(40f, 0f, 0f);
    }

    bool RaycastGrabbable(Vector3 origin, Vector3 dir, out RaycastHit bestHit, out Grabbable candidate)
    {
        var hits = Physics.RaycastAll(origin, dir, 5f, _player.GrabLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        
        foreach (var hit in hits)
        {
            var c = hit.collider.GetComponentInParent<Grabbable>();
            if (c != null && c.CanGrab)
            {
                if (c.Holder == this) continue; // Skip what THIS hand is already holding
                bestHit = hit;
                candidate = c;
                return true;
            }

            if (hit.collider.transform.IsChildOf(_player.transform)) continue;
            
            bestHit = hit;
            candidate = null;
            return false;
        }
        
        bestHit = default;
        candidate = null;
        return false;
    }

    void UpdatePushPullAndRotate()
    {
        if (Held == null) return;

        var commands = _player.Input.Commands;

        var rotate = _hand == XRHandSide.Left ? commands.RotateLeft : commands.RotateRight;
        var pushPull = _hand == XRHandSide.Left ? commands.PushPullLeft : commands.PushPullRight;

        if (Mathf.Abs(rotate) < 0.05f && Mathf.Abs(pushPull) < 0.05f)
        {
            _lockedAxis = 0;
        }

        if (_lockedAxis == 0)
        {
            if (Mathf.Abs(rotate) > 0.05f || Mathf.Abs(pushPull) > 0.05f)
            {
                if (Mathf.Abs(rotate) > Mathf.Abs(pushPull)) _lockedAxis = 1;
                else _lockedAxis = 2;
            }
        }

        if (!Held.UseDynamicAttach) rotate = 0f;

        if (_lockedAxis == 1) pushPull = 0f;
        else if (_lockedAxis == 2) rotate = 0f;
        else { rotate = 0f; pushPull = 0f; }

        if (Mathf.Abs(rotate) > 0.05f)
        {
            Held.transform.Rotate(Vector3.up, rotate * -135f * Time.deltaTime, Space.World);
        }

        if (!_isDistanceGrab) return;

        if (Mathf.Abs(pushPull) > 0.05f)
        {
            var offset = Held.transform.localPosition;
            var dist = offset.magnitude;
            dist += pushPull * 2f * Time.deltaTime; // 초당 2m 속도
            Held.transform.localPosition = offset.normalized * Mathf.Clamp(dist, 0.2f, 5f);
        }
    }

    void UpdateLaser(Transform anchor)
    {
        var line = anchor.GetComponent<LineRenderer>();
        if (line == null) return;

        if (Held != null)
        {
            line.enabled = false;
            return;
        }

        line.enabled = true;
        line.useWorldSpace = true;
        
        if (_player.LaserMaterial != null && line.sharedMaterial != _player.LaserMaterial)
            line.sharedMaterial = _player.LaserMaterial;

        Vector3 rayOrigin = anchor.position;
        Vector3 rayDir = anchor.forward;

        if (_player.Input.Commands.IsDesktop)
        {
            rayOrigin = _player.Head.position;
            rayDir = _player.Head.forward;
        }
        else
        {
            rayDir = anchor.rotation * Quaternion.Euler(40f, 0f, 0f) * Vector3.forward;
        }

        line.SetPosition(0, rayOrigin);

        if (RaycastGrabbable(rayOrigin, rayDir, out var hit, out var candidate))
        {
            line.SetPosition(1, hit.point);
            line.startColor = Color.red;
            line.endColor = Color.red;
        }
        else if (hit.collider != null) // Hit a non-grabbable object like a wall
        {
            line.SetPosition(1, hit.point);
            line.startColor = new Color(1f, 0f, 0f, 1f);
            line.endColor = new Color(1f, 0f, 0f, 0f);
        }
        else
        {
            line.SetPosition(1, rayOrigin + rayDir * 5f);
            line.startColor = new Color(1f, 0f, 0f, 1f);
            line.endColor = new Color(1f, 0f, 0f, 0f);
        }
    }

    void SetHovered(Grabbable target)
    {
        if (ReferenceEquals(_hovered, target)) return;

        if (_hovered != null) _hovered.SetHighlighted(false);
        _hovered = target;
        if (_hovered != null) _hovered.SetHighlighted(true);
    }

    void Grab(Grabbable target, bool distanceGrab)
    {
        if (!target.Attach(this, Anchor, distanceGrab)) return;

        Held = target;
        SetHovered(null);
        UserInput.Instance.SendHapticImpulse(_hand, 0.3f, 0.05f);
    }
}
