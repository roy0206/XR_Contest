using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

public enum KeyPhase
{
    Down,
    Up,
    Held
}

public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2
}

public enum XRHandSide
{
    Left = 0,
    Right = 1
}

public enum XRInputButton
{
    Select = 0,
    Grip = 1,
    Primary = 2,
    Secondary = 3,
    Menu = 4,
    Thumbstick = 5
}

/// <summary>
/// Unified Input System facade. Existing keyboard/mouse API is preserved for
/// editor testing while hand tracking is preferred over controller input on device.
/// </summary>
public class UserInput : Singleton<UserInput>
{
    sealed class KeyCallbacks
    {
        public Action Down;
        public Action Up;
        public Action Held;
    }

    readonly Dictionary<KeyCode, KeyCallbacks> keyCallbacks = new();
    readonly KeyCallbacks[] mouseCallbacks =
    {
        new(), new(), new()
    };

    readonly KeyCallbacks[,] xrCallbacks =
        new KeyCallbacks[2, Enum.GetValues(typeof(XRInputButton)).Length];
    readonly bool[,] xrCurrent =
        new bool[2, Enum.GetValues(typeof(XRInputButton)).Length];
    readonly bool[,] xrPrevious =
        new bool[2, Enum.GetValues(typeof(XRInputButton)).Length];

    readonly bool[] dragging = new bool[3];
    readonly Vector2[] dragOrigin = new Vector2[3];
    readonly List<UnityEngine.XR.InputDevice> xrDevices = new(2);
    readonly UnityEngine.XR.InputDevice[] controllers = new UnityEngine.XR.InputDevice[2];

    Vector2 prevMousePosition;

    public Vector2 MousePosition { get; private set; }
    public Vector2 MouseDelta { get; private set; }
    public Vector2 ScrollDelta { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (!ReferenceEquals(Instance, this)) return;

        for (var hand = 0; hand < xrCallbacks.GetLength(0); hand++)
            for (var button = 0; button < xrCallbacks.GetLength(1); button++)
                xrCallbacks[hand, button] = new KeyCallbacks();

        prevMousePosition = ReadMousePosition();
        MousePosition = prevMousePosition;
    }

    void Update()
    {
        UpdateMouse();
        UpdateXRState();
        DispatchKeyListeners();
        DispatchMouseListeners();
        DispatchXRListeners();
    }

    // Existing keyboard API, implemented with the Input System package.
    public bool GetKey(KeyCode key) => TryGetKeyControl(key, out var control) && control.isPressed;
    public bool GetKeyDown(KeyCode key) => TryGetKeyControl(key, out var control) && control.wasPressedThisFrame;
    public bool GetKeyUp(KeyCode key) => TryGetKeyControl(key, out var control) && control.wasReleasedThisFrame;

    public bool GetMouseButton(MouseButton button) => GetMouseControl(button)?.isPressed == true;
    public bool GetMouseButtonDown(MouseButton button) => GetMouseControl(button)?.wasPressedThisFrame == true;
    public bool GetMouseButtonUp(MouseButton button) => GetMouseControl(button)?.wasReleasedThisFrame == true;

    public bool IsDragging(MouseButton button) => dragging[(int)button];
    public Vector2 GetDragOrigin(MouseButton button) =>
        dragging[(int)button] ? dragOrigin[(int)button] : Vector2.zero;
    public Vector2 GetDragDelta(MouseButton button) =>
        dragging[(int)button] ? MousePosition - dragOrigin[(int)button] : Vector2.zero;

    public void AddKeyListener(KeyCode key, KeyPhase phase, Action callback)
    {
        if (callback == null) return;
        if (!keyCallbacks.TryGetValue(key, out var callbacks))
        {
            callbacks = new KeyCallbacks();
            keyCallbacks[key] = callbacks;
        }
        AddTo(callbacks, phase, callback);
    }

    public void RemoveKeyListener(KeyCode key, KeyPhase phase, Action callback)
    {
        if (callback != null && keyCallbacks.TryGetValue(key, out var callbacks))
            RemoveFrom(callbacks, phase, callback);
    }

    public void AddMouseListener(MouseButton button, KeyPhase phase, Action callback)
    {
        if (callback != null) AddTo(mouseCallbacks[(int)button], phase, callback);
    }

    public void RemoveMouseListener(MouseButton button, KeyPhase phase, Action callback)
    {
        if (callback != null) RemoveFrom(mouseCallbacks[(int)button], phase, callback);
    }

    // XR API. Hand pinch wins naturally because it is OR-ed with controller input.
    public bool GetXRButton(XRHandSide hand, XRInputButton button) => xrCurrent[(int)hand, (int)button];
    public bool GetXRButtonDown(XRHandSide hand, XRInputButton button) =>
        xrCurrent[(int)hand, (int)button] && !xrPrevious[(int)hand, (int)button];
    public bool GetXRButtonUp(XRHandSide hand, XRInputButton button) =>
        !xrCurrent[(int)hand, (int)button] && xrPrevious[(int)hand, (int)button];

    public void AddXRListener(XRHandSide hand, XRInputButton button, KeyPhase phase, Action callback)
    {
        if (callback != null) AddTo(xrCallbacks[(int)hand, (int)button], phase, callback);
    }

    public void RemoveXRListener(XRHandSide hand, XRInputButton button, KeyPhase phase, Action callback)
    {
        if (callback != null) RemoveFrom(xrCallbacks[(int)hand, (int)button], phase, callback);
    }

    public bool IsHandTracked(XRHandSide hand)
    {
        var aimHand = GetAimHand(hand);
        return aimHand != null && aimHand.isTracked.isPressed;
    }

    public float GetPinchStrength(XRHandSide hand)
    {
        var aimHand = GetAimHand(hand);
        return aimHand != null && aimHand.isTracked.isPressed
            ? aimHand.pinchStrengthIndex.ReadValue()
            : 0f;
    }

    /// <summary>Returns tracking-space pose. Use XR Origin transforms for world-space conversion.</summary>
    public bool TryGetXRDevicePose(XRHandSide hand, out Pose pose)
    {
        var aimHand = GetAimHand(hand);
        if (aimHand != null && aimHand.isTracked.isPressed)
        {
            pose = new Pose(aimHand.devicePosition.ReadValue(), aimHand.deviceRotation.ReadValue());
            return true;
        }

        var device = GetController(hand);
        if (device.isValid &&
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var position) &&
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rotation))
        {
            pose = new Pose(position, rotation);
            return true;
        }

        pose = Pose.identity;
        return false;
    }

    public Vector2 GetXRPrimaryAxis(XRHandSide hand)
    {
        var device = GetController(hand);
        return device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out var axis)
            ? axis
            : Vector2.zero;
    }

    public bool SendHapticImpulse(XRHandSide hand, float amplitude, float duration)
    {
        var device = GetController(hand);
        if (!device.isValid || !device.TryGetHapticCapabilities(out var capabilities) || !capabilities.supportsImpulse)
            return false;
        return device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Max(0f, duration));
    }

    void UpdateXRState()
    {
        for (var handIndex = 0; handIndex < 2; handIndex++)
        {
            var hand = (XRHandSide)handIndex;
            GetController(hand);
            for (var buttonIndex = 0; buttonIndex < xrCurrent.GetLength(1); buttonIndex++)
            {
                xrPrevious[handIndex, buttonIndex] = xrCurrent[handIndex, buttonIndex];
                xrCurrent[handIndex, buttonIndex] = ReadXRButton(hand, (XRInputButton)buttonIndex);
            }
        }
    }

    bool ReadXRButton(XRHandSide hand, XRInputButton button)
    {
        var aimHand = GetAimHand(hand);
        var handTracked = aimHand != null && aimHand.isTracked.isPressed;
        if (handTracked)
        {
            if (button == XRInputButton.Select && aimHand.indexPressed.isPressed) return true;
            if (button == XRInputButton.Grip && aimHand.middlePressed.isPressed) return true;
        }

        var device = GetController(hand);
        if (!device.isValid) return false;

        var usage = button switch
        {
            XRInputButton.Select => UnityEngine.XR.CommonUsages.triggerButton,
            XRInputButton.Grip => UnityEngine.XR.CommonUsages.gripButton,
            XRInputButton.Primary => UnityEngine.XR.CommonUsages.primaryButton,
            XRInputButton.Secondary => UnityEngine.XR.CommonUsages.secondaryButton,
            XRInputButton.Menu => UnityEngine.XR.CommonUsages.menuButton,
            _ => UnityEngine.XR.CommonUsages.primary2DAxisClick
        };
        return device.TryGetFeatureValue(usage, out var pressed) && pressed;
    }

    static MetaAimHand GetAimHand(XRHandSide hand) =>
        hand == XRHandSide.Left ? MetaAimHand.left : MetaAimHand.right;

    UnityEngine.XR.InputDevice GetController(XRHandSide hand)
    {
        var controller = controllers[(int)hand];
        if (controller.isValid) return controller;
        return RefreshController(hand);
    }

    UnityEngine.XR.InputDevice RefreshController(XRHandSide hand)
    {
        var characteristics = InputDeviceCharacteristics.Controller |
                              (hand == XRHandSide.Left ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right);
        xrDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(characteristics, xrDevices);
        for (var i = 0; i < xrDevices.Count; i++)
        {
            if (!xrDevices[i].isValid) continue;
            controllers[(int)hand] = xrDevices[i];
            return xrDevices[i];
        }

        controllers[(int)hand] = default;
        return controllers[(int)hand];
    }

    void UpdateMouse()
    {
        var current = ReadMousePosition();
        MouseDelta = current - prevMousePosition;
        MousePosition = current;
        prevMousePosition = current;
        ScrollDelta = Mouse.current?.scroll.ReadValue() ?? Vector2.zero;

        for (var i = 0; i < 3; i++)
        {
            var button = (MouseButton)i;
            if (GetMouseButtonDown(button))
            {
                dragging[i] = true;
                dragOrigin[i] = current;
            }
            else if (GetMouseButtonUp(button))
            {
                dragging[i] = false;
            }
        }
    }

    void DispatchKeyListeners()
    {
        foreach (var pair in keyCallbacks)
        {
            if (pair.Value.Down != null && GetKeyDown(pair.Key)) pair.Value.Down.Invoke();
            if (pair.Value.Up != null && GetKeyUp(pair.Key)) pair.Value.Up.Invoke();
            if (pair.Value.Held != null && GetKey(pair.Key)) pair.Value.Held.Invoke();
        }
    }

    void DispatchMouseListeners()
    {
        for (var i = 0; i < 3; i++)
        {
            var button = (MouseButton)i;
            var callbacks = mouseCallbacks[i];
            if (callbacks.Down != null && GetMouseButtonDown(button)) callbacks.Down.Invoke();
            if (callbacks.Up != null && GetMouseButtonUp(button)) callbacks.Up.Invoke();
            if (callbacks.Held != null && GetMouseButton(button)) callbacks.Held.Invoke();
        }
    }

    void DispatchXRListeners()
    {
        for (var hand = 0; hand < xrCallbacks.GetLength(0); hand++)
        for (var button = 0; button < xrCallbacks.GetLength(1); button++)
        {
            var callbacks = xrCallbacks[hand, button];
            var side = (XRHandSide)hand;
            var input = (XRInputButton)button;
            if (callbacks.Down != null && GetXRButtonDown(side, input)) callbacks.Down.Invoke();
            if (callbacks.Up != null && GetXRButtonUp(side, input)) callbacks.Up.Invoke();
            if (callbacks.Held != null && GetXRButton(side, input)) callbacks.Held.Invoke();
        }
    }

    static Vector2 ReadMousePosition() => Mouse.current?.position.ReadValue() ?? Vector2.zero;

    static UnityEngine.InputSystem.Controls.ButtonControl GetMouseControl(MouseButton button)
    {
        var mouse = Mouse.current;
        if (mouse == null) return null;
        return button switch
        {
            MouseButton.Left => mouse.leftButton,
            MouseButton.Right => mouse.rightButton,
            _ => mouse.middleButton
        };
    }

    static bool TryGetKeyControl(KeyCode keyCode, out UnityEngine.InputSystem.Controls.KeyControl control)
    {
        control = null;
        var keyboard = Keyboard.current;
        if (keyboard == null) return false;

        var name = keyCode.ToString();
        if (name.StartsWith("Alpha", StringComparison.Ordinal)) name = "Digit" + name.Substring(5);
        else if (name == "Return") name = "Enter";
        else if (name == "LeftControl") name = "LeftCtrl";
        else if (name == "RightControl") name = "RightCtrl";

        if (!Enum.TryParse<UnityEngine.InputSystem.Key>(name, out var key) || key == UnityEngine.InputSystem.Key.None)
            return false;

        control = keyboard[key];
        return control != null;
    }

    static void AddTo(KeyCallbacks callbacks, KeyPhase phase, Action callback)
    {
        switch (phase)
        {
            case KeyPhase.Down: callbacks.Down += callback; break;
            case KeyPhase.Up: callbacks.Up += callback; break;
            case KeyPhase.Held: callbacks.Held += callback; break;
        }
    }

    static void RemoveFrom(KeyCallbacks callbacks, KeyPhase phase, Action callback)
    {
        switch (phase)
        {
            case KeyPhase.Down: callbacks.Down -= callback; break;
            case KeyPhase.Up: callbacks.Up -= callback; break;
            case KeyPhase.Held: callbacks.Held -= callback; break;
        }
    }
}
