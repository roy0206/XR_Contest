using UnityEngine;

/// <summary>
/// Moves the player root. Direction comes from the head so desktop mouse look and the HMD
/// steer movement identically. Snap turn is used instead of smooth turn for comfort.
/// </summary>
public sealed class LocomotionModule : Module
{
    readonly Player _player;
    float _verticalVelocity;

    public LocomotionModule(Player player) : base(player) => _player = player;

    /// <summary>Set while a cutscene or forced sequence owns the player (F-003 3.4).</summary>
    public bool IsLocked { get; set; }

    public override void OnUpdate()
    {
        if (IsLocked) return;

        var isDesktop = _player.Input.Commands.IsDesktop;
        var rightHand = _player.GetHand(XRHandSide.Right);
        var snapTurn = (!isDesktop && rightHand != null && rightHand.Held != null) ? 0f : _player.Input.Commands.SnapTurn;
        
        if (!Mathf.Approximately(snapTurn, 0f))
            _player.transform.Rotate(Vector3.up, Mathf.Sign(snapTurn) * _player.SnapTurnAngle, Space.World);
    }

    public override void OnFixedUpdate()
    {
        var controller = _player.Controller;
        if (controller == null || !controller.enabled) return;

        var isDesktop = _player.Input.Commands.IsDesktop;
        var leftHand = _player.GetHand(XRHandSide.Left);
        var isLeftHeld = (!isDesktop && leftHand != null && leftHand.Held != null);
        var move = (IsLocked || isLeftHeld) ? Vector2.zero : _player.Input.Commands.Move;

        var forward = Vector3.ProjectOnPlane(_player.Head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = _player.transform.forward;
        forward.Normalize();
        var right = Vector3.Cross(Vector3.up, forward);

        var velocity = (forward * move.y + right * move.x) * _player.MoveSpeed;

        // A small downward bias keeps the controller grounded on slopes and step edges.
        if (controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -1f;
        else _verticalVelocity += _player.Gravity * Time.fixedDeltaTime;
        velocity.y = _verticalVelocity;

        controller.Move(velocity * Time.fixedDeltaTime);
    }
}
