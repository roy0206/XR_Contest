using System;
using UnityEngine;

/// <summary>
/// Anything a hand can pick up: tools, raw stock, work pieces. This holds grab state only.
/// Feedback and recovery are modules, so Chapter 1 processing behaviour can be added the same
/// way instead of by subclassing every tool.
/// </summary>
public class Grabbable : MonoThing
{
    [Header("Grab")]
    [Tooltip("Where the hand anchor lines up. Falls back to this transform.")]
    [SerializeField] Transform grabPoint;
    [SerializeField] bool grabEnabled = true;
    [Tooltip("Optional tighter limit than the hand's grab radius. 0 means the hand radius decides.")]
    [SerializeField, Min(0f)] float maxGrabDistance;

    [Header("Modules")]
    [SerializeField] bool highlightOnHover = true;
    [SerializeField] bool returnWhenLost = true;
    [SerializeField] bool useDynamicAttach = true;

    Transform _homeParent;
    Pose _homePose;
    bool _restKinematic;

    /// <summary>Raised after the object is attached to or detached from a hand.</summary>
    public event Action<Grabbable, GrabHandModule> Grabbed;
    public event Action<Grabbable, GrabHandModule> Released;
    public event Action<Grabbable> Activated;

    public Transform GrabPoint => grabPoint != null ? grabPoint : transform;

    /// <summary>Zero disables the per-object limit, leaving the hand's grab radius in charge.</summary>
    public float MaxGrabDistance => maxGrabDistance;
    public bool IsWithinGrabDistance(float distance) => maxGrabDistance <= 0f || distance <= maxGrabDistance;
    public GrabHandModule Holder { get; private set; }
    public bool IsHeld => Holder != null;
    public bool CanGrab => grabEnabled && isActiveAndEnabled;
    public Pose HomePose => _homePose;
    public bool UseDynamicAttach => useDynamicAttach;

    /// <summary>
    /// Process locking (F-014 5.3) turns this off so a tool for a later process cannot be picked up.
    /// Disabling it while held drops the object immediately.
    /// </summary>
    public bool GrabEnabled
    {
        get => grabEnabled;
        set
        {
            grabEnabled = value;
            if (!value) Holder?.Release();
        }
    }

    protected override void Awake()
    {
        base.Awake();

        _homeParent = transform.parent;
        _homePose = new Pose(transform.position, transform.rotation);
        _restKinematic = rigidbody3D != null && rigidbody3D.isKinematic;

        if (highlightOnHover) Attach(new GrabHighlightModule(this));
        if (returnWhenLost) Attach(new GrabReturnModule(this));
    }

    public void SetHighlighted(bool value) => GetModule<GrabHighlightModule>()?.SetHighlighted(value);

    /// <summary>Called by <see cref="GrabHandModule"/>. Returns false when the object is not grabbable.</summary>
    public bool Attach(GrabHandModule hand, Transform anchor, bool distanceGrab = false)
    {
        if (hand == null || anchor == null || !CanGrab) return false;

        if (Holder != null && Holder != hand)
        {
            Holder.Release();
        }

        Holder = hand;
        SetHighlighted(false);

        if (rigidbody3D != null)
        {
            rigidbody3D.linearVelocity = Vector3.zero;
            rigidbody3D.angularVelocity = Vector3.zero;
            rigidbody3D.isKinematic = true;
        }

        transform.SetParent(anchor, true);
        if (!distanceGrab) 
        {
            if (!useDynamicAttach)
            {
                AlignToAnchor(anchor);
            }
        }
        else if (!useDynamicAttach)
        {
            AlignRotationToRay(hand.GetRayRotation());
        }

        Grabbed?.Invoke(this, hand);
        return true;
    }

    public void Activate() => Activated?.Invoke(this);

    public void Detach(GrabHandModule hand)
    {
        if (Holder != null && Holder != hand) return;

        Holder = null;
        transform.SetParent(_homeParent, true);
        RestorePhysics();

        Released?.Invoke(this, hand);
    }

    /// <summary>Puts the object back where it started (F-005 1.8 recovery rule).</summary>
    public void ReturnHome()
    {
        Holder?.Release();

        transform.SetParent(_homeParent, true);
        transform.SetPositionAndRotation(_homePose.position, _homePose.rotation);
        RestorePhysics();
    }

    /// <summary>Freezes the object in place, e.g. once it is seated in a socket.</summary>
    public void Freeze(bool frozen)
    {
        if (rigidbody3D != null) rigidbody3D.isKinematic = frozen || _restKinematic;
        GrabEnabled = !frozen;
    }

    protected T Attach<T>(T module) where T : Module
    {
        AddModule(module);
        module.Init();
        return module;
    }

    void RestorePhysics()
    {
        if (rigidbody3D == null) return;

        rigidbody3D.isKinematic = _restKinematic;
        if (_restKinematic) return;

        rigidbody3D.linearVelocity = Vector3.zero;
        rigidbody3D.angularVelocity = Vector3.zero;
    }

    // Lines the grab point up with the anchor instead of snapping the object's own origin,
    void AlignToAnchor(Transform anchor)
    {
        var point = GrabPoint;
        if (point == transform)
        {
            transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            return;
        }

        var pointLocalRotation = Quaternion.Inverse(transform.rotation) * point.rotation;
        var pointLocalOffset = Quaternion.Inverse(transform.rotation) * (point.position - transform.position);

        var rotation = anchor.rotation * Quaternion.Inverse(pointLocalRotation);
        transform.SetPositionAndRotation(anchor.position - rotation * pointLocalOffset, rotation);
    }

    void AlignRotationToRay(Quaternion rayRotation)
    {
        var point = GrabPoint;
        if (point == transform)
        {
            transform.rotation = rayRotation;
            return;
        }

        var pointLocalRotation = Quaternion.Inverse(transform.rotation) * point.rotation;
        var rotation = rayRotation * Quaternion.Inverse(pointLocalRotation);
        transform.rotation = rotation;
    }
}
