using Fighter2D.Logic;
using Horizon.Core;
using Horizon.Engine;
using Silk.NET.Input;
using System.Numerics;

namespace Fighter2D.Player;



internal class PlayerInputTracker
{
    private struct InputState
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
    }
    private const int MaxInputHistory = 16;

    private readonly List<ButtonName> _frameButtonPresses = new();

    private readonly List<(ButtonName Btn, float Time)> _inputHistory = new();

    private readonly MoveList _moveList = new();
    private Horizon.Core.Collections.CircularBuffer<InputState> _circularInputHistory = new(MaxInputHistory);

    private IGamepad? _gamepad;
    private bool _prevDown;

    private IntervalRunner _runner;

    public PlayerInputTracker(int index)
    {
        _gamepad = GameEngine.Instance.InputManager.NativeInputContext.Gamepads[index];
        _runner = new IntervalRunner(1 / 60.0f, FixedUpdate);
        _inputHistory.RemoveAll(x => GameEngine.Instance.TotalTime - x.Time > 0.5f);
    }

    public bool IsConnected => _gamepad != null && _gamepad.IsConnected;

    public void ClearButtonHistory(ButtonName btn)
    {
        _inputHistory.RemoveAll(x => x.Btn == btn);
    }

    public ButtonName[] ConsumeFramePresses()
    {
        var presses = _frameButtonPresses.ToArray();
        _frameButtonPresses.Clear();
        return presses;
    }

    public Vector2 GetMovementInput()
    {
        if (_gamepad == null) return Vector2.Zero;

        return new Vector2(
            _gamepad.DPadLeft().Pressed ? -1 : _gamepad.DPadRight().Pressed ? 1 : 0,
            _gamepad.DPadUp().Pressed ? 1 : _gamepad.DPadDown().Pressed ? -1 : 0
        );
    }

    public bool HasDoubleTap(ButtonName btn, float window = 0.3f)
    {
        return _inputHistory.Count(x => x.Btn == btn && (GameEngine.Instance.TotalTime - x.Time) < window) >= 2;
    }

    public bool IsButtonHeld(ButtonName btn)
    {
        if (_gamepad != null && _gamepad.Buttons.Any(b => b.Name == btn && b.Pressed))
            return true;

        if (_gamepad != null)
        {
            if (btn == ButtonName.DPadLeft && _gamepad.DPadLeft().Pressed) return true;
            if (btn == ButtonName.DPadRight && _gamepad.DPadRight().Pressed) return true;
            if (btn == ButtonName.DPadUp && _gamepad.DPadUp().Pressed) return true;
            if (btn == ButtonName.DPadDown && _gamepad.DPadDown().Pressed) return true;
        }

        return false;
    }

    public void Update(float dt)
    {
        if (_gamepad is null && GameEngine.Instance.InputManager.NativeInputContext?.Gamepads.Count > 0)
        {
            _gamepad = GameEngine.Instance.InputManager.NativeInputContext.Gamepads[0];
            _gamepad.ButtonDown += (_, args) =>
            {
                RegisterButtonPress(args.Name);
            };
        }
    }

    public void Update()
    {
        _inputHistory.RemoveAll(x => GameEngine.Instance.TotalTime - x.Time > 0.5f);
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

    private void RegisterButtonPress(ButtonName btn)
    {
        _frameButtonPresses.Add(btn);
        _inputHistory.Add((btn, GameEngine.Instance.TotalTime));
    }
}