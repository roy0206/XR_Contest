using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Controller and hand-tracking source. <see cref="UserInput"/> already merges pinch input into
/// the XR buttons, so this only maps buttons and axes onto commands.
/// </summary>
public sealed class XRInputSource : IPlayerInputSource
{
    const float SnapTurnThreshold = 0.7f;
    const float SnapTurnReleaseThreshold = 0.3f;

    bool _snapTurnArmed = true;

    public bool IsAvailable => XRSettings.isDeviceActive;

    public void Read(ref PlayerCommands commands)
    {
        var input = UserInput.Instance;
        if (input == null) return;

        commands.Move = Vector2.ClampMagnitude(input.GetXRPrimaryAxis(XRHandSide.Left), 1f);
        commands.SnapTurn = ReadSnapTurn(input.GetXRPrimaryAxis(XRHandSide.Right).x);

        var leftAxis = input.GetXRPrimaryAxis(XRHandSide.Left);
        var rightAxis = input.GetXRPrimaryAxis(XRHandSide.Right);
        
        commands.PushPullLeft = leftAxis.y;
        commands.PushPullRight = rightAxis.y;
        commands.RotateLeft = leftAxis.x;
        commands.RotateRight = rightAxis.x;

        commands.SetGrab(XRHandSide.Left, ReadGrabPhase(input, XRHandSide.Left));
        commands.SetGrab(XRHandSide.Right, ReadGrabPhase(input, XRHandSide.Right));

        commands.InteractLeft = input.GetXRButtonDown(XRHandSide.Left, XRInputButton.Select);
        commands.InteractRight = input.GetXRButtonDown(XRHandSide.Right, XRInputButton.Select);
        commands.PushToTalk = input.GetXRButton(XRHandSide.Left, XRInputButton.Primary);
        commands.Pause = input.GetXRButtonDown(XRHandSide.Left, XRInputButton.Menu);

        // Right hand: the left Primary is already PTT, so a cutscene skip there would fight it.
        commands.Skip = input.GetXRButton(XRHandSide.Right, XRInputButton.Primary);

        if (input.TryGetXRDevicePose(XRHandSide.Left, out var leftPose))
            commands.SetHandPose(XRHandSide.Left, leftPose);
        if (input.TryGetXRDevicePose(XRHandSide.Right, out var rightPose))
            commands.SetHandPose(XRHandSide.Right, rightPose);
    }

    // Comfort turn: one step per stick push, re-armed only after the stick returns to centre.
    float ReadSnapTurn(float axis)
    {
        var magnitude = Mathf.Abs(axis);
        if (magnitude <= SnapTurnReleaseThreshold) _snapTurnArmed = true;
        if (!_snapTurnArmed || magnitude < SnapTurnThreshold) return 0f;

        _snapTurnArmed = false;
        return Mathf.Sign(axis);
    }

    static GrabPhase ReadGrabPhase(UserInput input, XRHandSide hand)
    {
        if (input.GetXRButtonDown(hand, XRInputButton.Grip)) return GrabPhase.Pressed;
        if (input.GetXRButtonUp(hand, XRInputButton.Grip)) return GrabPhase.Released;
        return input.GetXRButton(hand, XRInputButton.Grip) ? GrabPhase.Held : GrabPhase.None;
    }
}
