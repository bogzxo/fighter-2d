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

    public bool IsConnected => _gamepad != null && _gamepad.IsConnected;

    // Previous frame states for D-Pad polling (since XInput D-Pad is polled, not event-driven)
    private bool _prevLeft;
    private bool _prevRight;
    private bool _prevUp;
    private bool _prevDown;

    public PlayerInputTracker(int index)
    {
        _gamepad = GameEngine.Instance.InputManager.NativeInputContext.Gamepads[index];
        _gamepad.ButtonDown += (_, args) =>
        {
            RegisterButtonPress(args.Name);
        };

        _prevLeft = false;
        _prevRight = false;
        _prevUp = false;
        _prevDown = false;
    }

    public void Update()
    {
        // Poll D-Pad states from XInput joystick manager to support dpadleft/dpadright/dpadup/dpaddown bindings & double-taps
        if (_gamepad != null)
        {
            bool leftPressed = _gamepad.DPadLeft().Pressed;
            bool rightPressed = _gamepad.DPadRight().Pressed;
            bool upPressed = _gamepad.DPadUp().Pressed;
            bool downPressed = _gamepad.DPadDown().Pressed;

            if (leftPressed && !_prevLeft) RegisterButtonPress(ButtonName.DPadLeft);
            if (rightPressed && !_prevRight) RegisterButtonPress(ButtonName.DPadRight);
            if (upPressed && !_prevUp) RegisterButtonPress(ButtonName.DPadUp);
            if (downPressed && !_prevDown) RegisterButtonPress(ButtonName.DPadDown);

            _prevLeft = leftPressed;
            _prevRight = rightPressed;
            _prevUp = upPressed;
            _prevDown = downPressed;
        }

        _inputHistory.RemoveAll(x => GameEngine.Instance.TotalTime - x.Time > 0.5f);
    }

    private void RegisterButtonPress(ButtonName btn)
    {
        _frameButtonPresses.Add(btn);
        _inputHistory.Add((btn, GameEngine.Instance.TotalTime));
    }

    public ButtonName[] ConsumeFramePresses()
    {
        var presses = _frameButtonPresses.ToArray();
        _frameButtonPresses.Clear();
        return presses;
    }

    public bool HasDoubleTap(ButtonName btn, float window = 0.3f)
    {
        return _inputHistory.Count(x => x.Btn == btn && (GameEngine.Instance.TotalTime - x.Time) < window) >= 2;
    }

    public void ClearButtonHistory(ButtonName btn)
    {
        _inputHistory.RemoveAll(x => x.Btn == btn);
    }

    public bool IsButtonHeld(ButtonName btn)
    {
        if (_gamepad != null && _gamepad.Buttons.Any(b => b.Name == btn && b.Pressed))
            return true;

        //var joystick = XInputJoystickInputManager.Gamepad;
        if (_gamepad != null)
        {
            if (btn == ButtonName.DPadLeft && _gamepad.DPadLeft().Pressed) return true;
            if (btn == ButtonName.DPadRight && _gamepad.DPadRight().Pressed) return true;
            if (btn == ButtonName.DPadUp && _gamepad.DPadUp().Pressed) return true;
            if (btn == ButtonName.DPadDown && _gamepad.DPadDown().Pressed) return true;
        }

        return false;
    }

    public Vector2 GetMovementInput()
    {
        if (_gamepad == null) return Vector2.Zero;

        return new Vector2(
            _gamepad.DPadLeft().Pressed ? -1 : _gamepad.DPadRight().Pressed ? 1 : 0,
            _gamepad.DPadUp().Pressed ? 1 : _gamepad.DPadDown().Pressed ? -1 : 0
        );
    }
}