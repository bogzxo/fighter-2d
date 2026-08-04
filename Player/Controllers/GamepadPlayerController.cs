using Fighter2D.Logic;
using Fighter2D.Scenes;
using Horizon.Core;
using Horizon.Core.Collections;
using Horizon.Engine;
using Silk.NET.Input;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Fighter2D.Player.Controllers
{
    internal class GamepadPlayerController : PlayerController
    {
        private const int MaxInputHistory = 16;
        private CircularBuffer<InputFlags> _circularInputFlags = new(MaxInputHistory);
        private IGamepad? _gamepad;
        public bool IsConnected => _gamepad != null && _gamepad.IsConnected;

        private readonly List<ButtonName> _frameButtonPresses = new();
        private readonly List<(ButtonName Btn, float Time)> _inputHistory = new();

        public GamepadPlayerController(int index, MoveList moveList) : base(moveList)
        {
            _gamepad = GameEngine.Instance.InputManager.NativeInputContext.Gamepads[index];
            _gamepad.ButtonDown += (_, args) =>
            {
                RegisterButtonPress(args.Name);
            };
        }

        protected override void FixedUpdate()
        {
            // (bool left, bool up, bool down, bool right, bool a_button, bool b_button, bool x_button, bool y_button

            // save current state of the player input
            InputFlags current = (
                (_gamepad?.DPadLeft().Pressed ?? false ? InputFlags.DPadLeft : InputFlags.None) |
                (_gamepad?.DPadRight().Pressed ?? false ? InputFlags.DPadRight : InputFlags.None) |
                (_gamepad?.DPadUp().Pressed ?? false ? InputFlags.DPadUp : InputFlags.None) |
                (_gamepad?.DPadDown().Pressed ?? false ? InputFlags.DPadDown : InputFlags.None) |
                ((_gamepad?.A().Pressed ?? false) & FightScene.ControlledPlayer.Controller.MoveAnimationFinishedEvent.IsSet ? InputFlags.A : InputFlags.None) |
                ((_gamepad?.B().Pressed ?? false) & FightScene.ControlledPlayer.Controller.MoveAnimationFinishedEvent.IsSet ? InputFlags.B : InputFlags.None) |
                ((_gamepad?.X().Pressed ?? false) & FightScene.ControlledPlayer.Controller.MoveAnimationFinishedEvent.IsSet ? InputFlags.X : InputFlags.None) |
                ((_gamepad?.Y().Pressed ?? false) & FightScene.ControlledPlayer.Controller.MoveAnimationFinishedEvent.IsSet ? InputFlags.Y : InputFlags.None)
            );

            _circularInputFlags.Append(current);
            Console.WriteLine(current);

            // squash into just the diffs for that whole array
            List<InputFlags> inputStates = new List<InputFlags>();
            foreach (var inputState in _circularInputFlags.ToArray())
            {
                if (!inputStates.Contains(inputState))
                    inputStates.Add(inputState);
            }

            // match it to a move in an order of priority
            foreach (var (name, move) in MoveList.FightingMoves)
            {
                if (move.InputSignature == InputFlags.None) continue;
                foreach (var inputState in inputStates)
                {
                    if ((move.InputSignature ^ inputState) == InputFlags.None)
                    {
                        //Console.WriteLine(name);
                        //controller.domove

                    }
                }
            }
            _inputHistory.RemoveAll(x => GameEngine.Instance.TotalTime - x.Time > 0.5f);

            var movementDir = GetMovementInput();

            if (movementDir.X != 0)
            {
                Player.Flipped = movementDir.X < 0;
            }

            // Apply velocity-based movement with damping
            if (movementDir.X != 0)
            {
                var targetVelocity = movementDir.X * PlayerConfig.WALK_SPEED;
                var currentVelocityX = Player.PhysicsBody.Velocity.X;
                var velocityDiff = targetVelocity - currentVelocityX;

                // Apply force proportional to velocity difference for responsive control
                Player.PhysicsBody.ApplyForce(new Vector2(velocityDiff * Player.PhysicsBody.Mass * 5f, 0));
            }
        }

        internal override bool IsMoveHeld(FightingMove candidate)
        {

            //if (candidate.Bindings.Length == 0) return false;

            //return candidate.UseAnyBindings
            //    ? candidate.Bindings.Any(_input.IsButtonHeld)
            //    : candidate.Bindings.All(_input.IsButtonHeld);
            return false;
        }

        public override void TryProcessNewInputs(float dt)
        {
            //var buttons = _input.ConsumeFramePresses();
            //if (buttons.Length == 0 || !CurrentMove.Interuptable) return;

            //foreach (var candidate in MoveList.FightingMoves.Values)
            //{
            //    if (candidate.Bindings.Length == 0) continue;

            //    bool matched = candidate.UseAnyBindings
            //        ? candidate.Bindings.Any(buttons.Contains)
            //        : candidate.Bindings.All(buttons.Contains);

            //    if (matched)
            //    {
            //        FightingMove moveToExecute = candidate;

            //        if (!IsStanceValid(candidate.Stances))
            //        {
            //            if (candidate.StanceReroutes != null &&
            //                candidate.StanceReroutes.TryGetValue(StateTracker.CurrentStance, out string? reroutedName) &&
            //                MoveList.FightingMoves.TryGetValue(reroutedName, out var reroutedMove))
            //            {
            //                moveToExecute = reroutedMove;
            //            }
            //            else continue;
            //        }

            //        if (CurrentMove.Name == moveToExecute.Name) continue;

            //        if (candidate.DoubleTap || moveToExecute.DoubleTap)
            //        {
            //            bool isDoubleTap = false;
            //            foreach (var btn in candidate.Bindings.Where(buttons.Contains))
            //            {
            //                if (_input.HasDoubleTap(btn, 0.3f))
            //                {
            //                    isDoubleTap = true;
            //                    _input.ClearButtonHistory(btn);
            //                    break;
            //                }
            //            }
            //            if (!isDoubleTap) continue;
            //        }

            //        ChangeToMove(moveToExecute);
            //        return;
            //    }
            //}
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
}