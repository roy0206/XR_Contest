using UnityEngine;

/// <summary>
/// Desktop-only head aiming. In XR the HMD owns the head pose, so this module idles instead of
/// fighting the tracked camera.
/// </summary>
public sealed class ViewModule : Module
{
    readonly Player _player;
    float _pitch;

    public ViewModule(Player player) : base(player)
    {
        _player = player;
        Application.onBeforeRender += UpdateVRTracking;
    }

    public override void OnRemoved()
    {
        Application.onBeforeRender -= UpdateVRTracking;
    }

    void UpdateVRTracking()
    {
        var commands = _player.Input.Commands;
        var head = _player.Head;
        if (commands.IsDesktop || IsLocked || head == _player.transform) return;

        var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.CenterEye);
        if (device.isValid)
        {
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var pos))
                head.localPosition = pos;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rot))
                head.localRotation = rot;
        }
    }

    public bool IsLocked { get; set; }

    public override void OnUpdate()
    {
        var commands = _player.Input.Commands;
        var head = _player.Head;
        if (head == _player.transform) return;

        if (!commands.IsDesktop || IsLocked) return;

        var look = commands.Look * _player.LookSensitivity;
        if (look.sqrMagnitude > 0f)
        {
            // Yaw turns the whole body so movement direction follows the view.
            _player.transform.Rotate(Vector3.up, look.x, Space.World);
            _pitch = Mathf.Clamp(_pitch - look.y, -_player.PitchLimit, _player.PitchLimit);
        }

        head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }
}
