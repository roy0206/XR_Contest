using UnityEngine;

/// <summary>
/// Player root. Holds references and tuning values only; every behaviour lives in a Module.
/// Module order matters: <see cref="PlayerInputModule"/> is added first so the rest of the
/// modules read commands that were refreshed this frame.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class Player : MonoThing
{
    [Header("References")]
    [SerializeField] Transform head;
    [SerializeField] Transform leftHandAnchor;
    [SerializeField] Transform rightHandAnchor;

    [Header("Locomotion")]
    [SerializeField, Min(0f)] float moveSpeed = 2.2f;
    [SerializeField, Min(0f)] float snapTurnAngle = 45f;
    [SerializeField] float gravity = -9.81f;

    [Header("View")]
    [SerializeField, Min(0f)] float lookSensitivity = 2.5f;
    [SerializeField, Range(30f, 89f)] float pitchLimit = 85f;

    [Header("Hands")]
    [Tooltip("Desktop hand anchor offset from the head. x is mirrored for the left hand.")]
    [SerializeField] Vector3 desktopHandOffset = new(0.25f, -0.3f, 0.55f);
    [SerializeField, Min(0f)] float grabRadius = 0.35f;
    [SerializeField] LayerMask grabLayers = ~0;
    [Tooltip("How long a held object keeps its last valid pose after tracking is lost (F-005 1.7).")]
    [SerializeField, Min(0f)] float trackingLossGrace = 1f;

    public CharacterController Controller { get; private set; }
    public PlayerInputModule Input { get; private set; }
    public LocomotionModule Locomotion { get; private set; }
    public ViewModule View { get; private set; }

    public Transform Head => head != null ? head : transform;
    public float MoveSpeed => moveSpeed;
    public float SnapTurnAngle => snapTurnAngle;
    public float Gravity => gravity;
    public float LookSensitivity => lookSensitivity;
    public float PitchLimit => pitchLimit;
    public Vector3 DesktopHandOffset => desktopHandOffset;
    public float GrabRadius => grabRadius;
    public LayerMask GrabLayers => grabLayers;
    public float TrackingLossGrace => trackingLossGrace;

    protected override void Awake()
    {
        base.Awake();

        Controller = GetComponent<CharacterController>();
        if (head == null)
        {
            var camera = GetComponentInChildren<Camera>();
            head = camera != null ? camera.transform : transform;
        }

        Input = Attach(new PlayerInputModule(this));
        Locomotion = Attach(new LocomotionModule(this));
        View = Attach(new ViewModule(this));
        Attach(new GrabHandModule(this, XRHandSide.Left));
        Attach(new GrabHandModule(this, XRHandSide.Right));
        Attach(new VoiceInputModule(this));
    }

    public Transform GetHandAnchor(XRHandSide hand) =>
        hand == XRHandSide.Left ? leftHandAnchor : rightHandAnchor;

    public GrabHandModule GetHand(XRHandSide hand)
    {
        foreach (var module in GetModules<GrabHandModule>())
            if (module.Hand == hand) return module;
        return null;
    }

    // Draws the reach of both hands so an out-of-range grab is obvious in the scene view.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        DrawHandGizmo(XRHandSide.Left);
        DrawHandGizmo(XRHandSide.Right);
    }

    void DrawHandGizmo(XRHandSide hand)
    {
        var anchor = GetHandAnchor(hand);
        if (anchor != null) Gizmos.DrawWireSphere(anchor.position, grabRadius);
    }

    T Attach<T>(T module) where T : Module
    {
        AddModule(module);
        module.Init();
        return module;
    }
}
