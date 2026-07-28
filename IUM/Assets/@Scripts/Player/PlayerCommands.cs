using UnityEngine;

/// <summary>Per-frame state of a grab request, translated from whatever button a source uses.</summary>
public enum GrabPhase
{
    None,
    Pressed,
    Held,
    Released
}

/// <summary>
/// Device-independent player intent for a single frame. Every input source writes this and
/// every gameplay module reads it, so desktop and XR run the exact same judgement path.
/// </summary>
public struct PlayerCommands
{
    /// <summary>x: strafe, y: forward. Already clamped to a unit circle.</summary>
    public Vector2 Move;

    /// <summary>Desktop look delta in degrees. XR leaves this at zero because the HMD drives the head.</summary>
    public Vector2 Look;

    /// <summary>-1, 0 or 1. Fires once per intent instead of every frame.</summary>
    public float SnapTurn;

    public GrabPhase LeftGrab;
    public GrabPhase RightGrab;
    public bool Interact;
    public bool PushToTalk;
    public bool Pause;

    /// <summary>True while the desktop source is active, which also means no HMD is driving the head.</summary>
    public bool IsDesktop;

    /// <summary>Tracking-space hand poses. Only valid when the matching Has* flag is set.</summary>
    public Pose LeftHandPose;
    public Pose RightHandPose;
    public bool HasLeftHandPose;
    public bool HasRightHandPose;

    public GrabPhase GetGrab(XRHandSide hand) => hand == XRHandSide.Left ? LeftGrab : RightGrab;

    public void SetGrab(XRHandSide hand, GrabPhase phase)
    {
        if (hand == XRHandSide.Left) LeftGrab = phase;
        else RightGrab = phase;
    }

    public bool HasHandPose(XRHandSide hand) => hand == XRHandSide.Left ? HasLeftHandPose : HasRightHandPose;

    public bool TryGetHandPose(XRHandSide hand, out Pose pose)
    {
        if (hand == XRHandSide.Left)
        {
            pose = LeftHandPose;
            return HasLeftHandPose;
        }

        pose = RightHandPose;
        return HasRightHandPose;
    }

    public void SetHandPose(XRHandSide hand, Pose pose)
    {
        if (hand == XRHandSide.Left)
        {
            LeftHandPose = pose;
            HasLeftHandPose = true;
        }
        else
        {
            RightHandPose = pose;
            HasRightHandPose = true;
        }
    }

    public void Clear() => this = default;
}
