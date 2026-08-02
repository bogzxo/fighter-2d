using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Horizon.Core;
using Horizon.Engine;
using Horizon.Input.Components;

using Silk.NET.Input;

namespace Fighter2D.Player;

internal class PlayerInputTracker
{
    private struct InputState : IEquatable<InputState>
    {
        public bool A_Button;
        public bool B_Button;
        public bool Down;
        public bool Left;
        public bool Right;
        public bool Up;
        public bool X_Button;
        public bool Y_Button;

        public InputState(bool left, bool up, bool down, bool right, bool a_button, bool b_button, bool x_button, bool y_button)
        {
            Left = left;
            Up = up;
            Down = down;
            Right = right;
            A_Button = a_button;
            B_Button = b_button;
            X_Button = x_button;
            Y_Button = y_button;
        }

        public bool Equals(InputState other)
        {
            return A_Button == other.A_Button && B_Button == other.B_Button && Down == other.Down && Left == other.Left && Right == other.Right && Up == other.Up && X_Button == other.X_Button && Y_Button == other.Y_Button;
        }

        public override bool Equals(object? obj)
        {
            return obj is InputState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(A_Button, B_Button, Down, Left, Right, Up, X_Button, Y_Button);
        }
    }

    private const int MaxInputHistory = 16;
    private Horizon.Core.Collections.CircularBuffer<InputState> _circularInputHistory = new(MaxInputHistory);

    private IntervalRunner _runner;
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
        _runner = new IntervalRunner(1 / 60.0f, FixedUpdate);
    }

    private void FixedUpdate()
    {
        // (bool left, bool up, bool down, bool right, bool a_button, bool b_button, bool x_button, bool y_button)

        // save current state of the player input
        _circularInputHistory.Append(new InputState(
            _gamepad?.DPadLeft().Pressed ?? false,
            _gamepad?.DPadUp().Pressed ?? false,
            _gamepad?.DPadDown().Pressed ?? false,
            _gamepad?.DPadRight().Pressed ?? false,
            _gamepad?.Buttons.FirstOrDefault(b => b.Name == ButtonName.A).Pressed ?? false,
            _gamepad?.Buttons.FirstOrDefault(b => b.Name == ButtonName.B).Pressed ?? false,
            _gamepad?.Buttons.FirstOrDefault(b => b.Name == ButtonName.X).Pressed ?? false,
            _gamepad?.Buttons.FirstOrDefault(b => b.Name == ButtonName.Y).Pressed ?? false
        ));

        // squash into just the diffs for that whole array
        List<InputState> inputStates = new List<InputState>();
        foreach (var inputState in _circularInputHistory.ToArray())
        {
            if (!inputStates.Contains(inputState))
                inputStates.Add(inputState);
        }

        // match it to a move in an order of priority

        foreach (var inputState in inputStates)
        {
            // in an ideal world:
            //var move = _moveList.MatchInput(inputState);
            //if (move != null)
            //{
            //    // trigger the move
            //    GameEngine.Instance.TriggerMove(move);
            //    break;
            //}
        }
    }


    public void Update(float dt)
    {
        _runner.UpdateState(dt);
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