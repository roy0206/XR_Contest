using UnityEngine;

/// <summary>
/// Keyboard and mouse source. This is a maintained development path, not debug code:
/// every core interaction must stay reachable without an HMD.
///
/// 이동 W/A/S/D · 시점 마우스(우클릭 유지) · 스냅 턴 Q/E ·
/// 왼손 잡기 F · 오른손 잡기 마우스 좌클릭 · 상호작용 R · PTT T · 일시정지 Esc
/// </summary>
public sealed class DesktopInputSource : IPlayerInputSource
{
    // Mouse delta arrives in pixels; this brings it into a degrees-per-frame range.
    const float LookScale = 0.1f;

    public bool IsAvailable => true;

    public void Read(ref PlayerCommands commands)
    {
        var input = UserInput.Instance;
        if (input == null) return;

        commands.IsDesktop = true;

        var move = Vector2.zero;
        if (input.GetKey(KeyCode.W)) move.y += 1f;
        if (input.GetKey(KeyCode.S)) move.y -= 1f;
        if (input.GetKey(KeyCode.D)) move.x += 1f;
        if (input.GetKey(KeyCode.A)) move.x -= 1f;
        commands.Move = Vector2.ClampMagnitude(move, 1f);

        // Holding right mouse keeps the editor cursor usable while still allowing free look.
        if (input.GetMouseButton(MouseButton.Right))
            commands.Look = input.MouseDelta * LookScale;

        if (input.GetKeyDown(KeyCode.Q)) commands.SnapTurn = -1f;
        else if (input.GetKeyDown(KeyCode.E)) commands.SnapTurn = 1f;

        commands.SetGrab(XRHandSide.Left, ReadKeyPhase(input, KeyCode.F));
        commands.SetGrab(XRHandSide.Right, ReadMousePhase(input, MouseButton.Left));

        commands.Interact = input.GetKeyDown(KeyCode.R);
        commands.PushToTalk = input.GetKey(KeyCode.T);
        commands.Pause = input.GetKeyDown(KeyCode.Escape);
    }

    static GrabPhase ReadKeyPhase(UserInput input, KeyCode key) =>
        ToPhase(input.GetKeyDown(key), input.GetKey(key), input.GetKeyUp(key));

    static GrabPhase ReadMousePhase(UserInput input, MouseButton button) =>
        ToPhase(input.GetMouseButtonDown(button), input.GetMouseButton(button), input.GetMouseButtonUp(button));

    static GrabPhase ToPhase(bool down, bool held, bool up)
    {
        if (down) return GrabPhase.Pressed;
        if (up) return GrabPhase.Released;
        return held ? GrabPhase.Held : GrabPhase.None;
    }
}
