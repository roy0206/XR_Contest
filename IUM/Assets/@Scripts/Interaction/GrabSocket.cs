using UnityEngine;

/// <summary>
/// Trigger volume that accepts a released <see cref="Grabbable"/>. Used for "put the tool here"
/// steps and later for work-bench and install positions.
/// Trigger messages are Unity callbacks so they stay on the component; the judgement itself
/// lives in <see cref="SocketPlacementModule"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class GrabSocket : MonoThing
{
    [SerializeField] Transform attachPoint;
    [Tooltip("Leave empty to accept any grabbable that enters.")]
    [SerializeField] Grabbable requiredTarget;
    [Tooltip("Freeze the object once it is seated so it cannot be picked up again.")]
    [SerializeField] bool lockOnPlace;

    public SocketPlacementModule Placement { get; private set; }
    public Transform AttachPoint => attachPoint != null ? attachPoint : transform;
    public Grabbable RequiredTarget => requiredTarget;
    public bool LockOnPlace => lockOnPlace;

    protected override void Awake()
    {
        base.Awake();

        if (collider3D != null) collider3D.isTrigger = true;

        Placement = new SocketPlacementModule(this);
        AddModule(Placement);
        Placement.Init();
    }

    void OnTriggerEnter(Collider other) =>
        Placement?.AddCandidate(other.GetComponentInParent<Grabbable>());

    void OnTriggerExit(Collider other) =>
        Placement?.RemoveCandidate(other.GetComponentInParent<Grabbable>());
}
