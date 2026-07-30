using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Horizon.Engine;
using Horizon.Input.Components;

using Silk.NET.Input;

namespace Fighter2D.Player;

internal class PlayerInputTracker
{
    private readonly List<ButtonName> _frameButtonPresses = new();
    private readonly List<(ButtonName Btn, float Time)> _inputHistory = new();
    private IGamepad? _gamepad;

    // Previous frame states for D-Pad polling (since XInput D-Pad is polled, not event-driven)
    private bool _prevLeft;
    private bool _prevRight;
    private bool _prevUp;
    private bool _prevDown;

    public void Update(float totalEngineTime)
    {
        if (_gamepad is null && GameEngine.Instance.InputManager.NativeInputContext?.Gamepads.Count > 0)
        {
            _gamepad = GameEngine.Instance.InputManager.NativeInputContext.Gamepads[0];
            _gamepad.ButtonDown += (_, args) =>
            {
                RegisterButtonPress(args.Name, totalEngineTime);
            };
        }

        // Poll D-Pad states from XInput joystick manager to support dpadleft/dpadright/dpadup/dpaddown bindings & double-taps
        var joystick = XInputJoystickInputManager.Gamepad;
        if (joystick != null)
        {
            bool leftPressed = joystick.DPadLeft().Pressed;
            bool rightPressed = joystick.DPadRight().Pressed;
            bool upPressed = joystick.DPadUp().Pressed;
            bool downPressed = joystick.DPadDown().Pressed;

            if (leftPressed && !_prevLeft) RegisterButtonPress(ButtonName.DPadLeft, totalEngineTime);
            if (rightPressed && !_prevRight) RegisterButtonPress(ButtonName.DPadRight, totalEngineTime);
            if (upPressed && !_prevUp) RegisterButtonPress(ButtonName.DPadUp, totalEngineTime);
            if (downPressed && !_prevDown) RegisterButtonPress(ButtonName.DPadDown, totalEngineTime);

            _prevLeft = leftPressed;
            _prevRight = rightPressed;
            _prevUp = upPressed;
            _prevDown = downPressed;
        }

        _inputHistory.RemoveAll(x => totalEngineTime - x.Time > 0.5f);
    }

    private void RegisterButtonPress(ButtonName btn, float time)
    {
        _frameButtonPresses.Add(btn);
        _inputHistory.Add((btn, time));
    }

    public ButtonName[] ConsumeFramePresses()
    {
        var presses = _frameButtonPresses.ToArray();
        _frameButtonPresses.Clear();
        return presses;
    }

    public bool HasDoubleTap(ButtonName btn, float totalEngineTime, float window = 0.3f)
    {
        return _inputHistory.Count(x => x.Btn == btn && (totalEngineTime - x.Time) < window) >= 2;
    }

    public void ClearButtonHistory(ButtonName btn)
    {
        _inputHistory.RemoveAll(x => x.Btn == btn);
    }

    public bool IsButtonHeld(ButtonName btn)
    {
        if (_gamepad != null && _gamepad.Buttons.Any(b => b.Name == btn && b.Pressed))
            return true;

        var joystick = XInputJoystickInputManager.Gamepad;
        if (joystick != null)
        {
            if (btn == ButtonName.DPadLeft && joystick.DPadLeft().Pressed) return true;
            if (btn == ButtonName.DPadRight && joystick.DPadRight().Pressed) return true;
            if (btn == ButtonName.DPadUp && joystick.DPadUp().Pressed) return true;
            if (btn == ButtonName.DPadDown && joystick.DPadDown().Pressed) return true;
        }

        return false;
    }

    public Vector2 GetMovementInput()
    {
        var joystick = XInputJoystickInputManager.Gamepad;
        if (joystick == null) return Vector2.Zero;

        return new Vector2(
            joystick.DPadLeft().Pressed ? -1 : joystick.DPadRight().Pressed ? 1 : 0,
            joystick.DPadUp().Pressed ? 1 : joystick.DPadDown().Pressed ? -1 : 0
        );
    }
}