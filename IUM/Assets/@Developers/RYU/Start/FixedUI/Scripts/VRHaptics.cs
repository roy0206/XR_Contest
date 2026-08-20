using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public static class VRHaptics
{
	static readonly List<InputDevice> devices = new List<InputDevice>();

	public static void Pulse(float amplitude, float duration)
	{
		devices.Clear();
		InputDevices.GetDevicesWithCharacteristics(
			InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand,
			devices);

		foreach (var d in devices)
		{
			if (d.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
				d.SendHapticImpulse(0u, amplitude, duration);
		}
	}
}