using UnityEngine;

/// <summary>
/// Single point where an input device becomes game intent. Picks the active source each frame
/// and keeps the hand anchors in sync, so no other module ever touches an XR API.
/// </summary>
public sealed class PlayerInputModule : Module
{
    readonly Player _player;
    readonly DesktopInputSource _desktop = new();
    readonly XRInputSource _xr = new();

    PlayerCommands _commands;

    public PlayerInputModule(Player player) : base(player) => _player = player;

    public PlayerCommands Commands => _commands;
    public bool IsDesktop => _commands.IsDesktop;

    public override void OnUpdate()
    {
        _commands.Clear();

        // XR wins when a device is running; otherwise the editor falls back to desktop
        // automatically instead of leaving the scene unplayable.
        IPlayerInputSource source = _xr.IsAvailable ? _xr : _desktop;
        source.Read(ref _commands);

        ApplyHandPose(XRHandSide.Left);
        ApplyHandPose(XRHandSide.Right);
    }

    /// <summary>Desktop hands are always "tracked"; XR hands report their real tracking state.</summary>
    public bool IsHandTracked(XRHandSide hand) => _commands.IsDesktop || _commands.HasHandPose(hand);

    void ApplyHandPose(XRHandSide hand)
    {
        var anchor = _player.GetHandAnchor(hand);
        if (anchor == null) return;

        if (_commands.IsDesktop)
        {
            // Anchors are children of the player root, so keeping them in front of the head
            // has to be done explicitly. XR overwrites this with the real device pose below.
            var offset = _player.DesktopHandOffset;
            if (hand == XRHandSide.Left) offset.x = -offset.x;

            var headTransform = _player.Head;
            anchor.SetPositionAndRotation(headTransform.TransformPoint(offset), headTransform.rotation);
            return;
        }

        // Device poses are tracking-space, which is the player root's local space.
        if (_commands.TryGetHandPose(hand, out var pose))
        {
            anchor.localPosition = pose.position;
            anchor.localRotation = pose.rotation;
        }
    }
}
