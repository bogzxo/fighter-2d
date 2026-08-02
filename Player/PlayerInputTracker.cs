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

    public PlayerInputTracker(int index)
    {
        _gamepad = GameEngine.Instance.InputManager.NativeInputContext.Gamepads[index];
        _gamepad.ButtonDown += (_, args) =>
        {
            RegisterButtonPress(args.Name);
        };
    }

    public void Update()
    {
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
        if (_gamepad == null) return false;

        switch (btn)
        {
            case ButtonName.DPadLeft when _gamepad.DPadLeft().Pressed:
            case ButtonName.DPadRight when _gamepad.DPadRight().Pressed:
            case ButtonName.DPadUp when _gamepad.DPadUp().Pressed:
            case ButtonName.DPadDown when _gamepad.DPadDown().Pressed:
                return true;
            default:
                return false;
        }
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