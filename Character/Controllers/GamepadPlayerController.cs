using System.Linq;
using System.Numerics;

using Fighter2D.Character.Controllers;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;

using Horizon.Engine;

using Silk.NET.Input;

namespace Fighter2D.Character.Controllers;

internal class GamepadPlayerController : PlayerController
{
    private readonly IGamepad? _gamepad;

    public GamepadPlayerController(int index, MoveList moveList) : base(moveList)
    {
        _gamepad = GameEngine.Instance.InputManager.NativeInputContext.Gamepads.ElementAtOrDefault(index);
    }

    public override void TryProcessNewInputs(float dt)
    {
        // Attacks > Evasion > Jumping > Movement > Crouch
        if (IsButtonHeld(ButtonName.A))
        {
            AttemptMove(MoveId.KickLeft);
        }
        else if (IsButtonHeld(ButtonName.DPadUp) && StateTracker.IsGrounded)
        {
            AttemptMove(MoveId.Jump);
        }
        else if (IsButtonHeld(ButtonName.DPadDown))
        {
            AttemptMove(MoveId.Crouch);
        }
        else if (IsButtonHeld(ButtonName.DPadLeft) || IsButtonHeld(ButtonName.DPadRight))
        {
            // Optional: You might want to restrict running in the air too
            if (StateTracker.IsGrounded)
            {
                AttemptMove(MoveId.Run);
            }
        }
    }

    private void AttemptMove(MoveId candidateId)
    {
        if (MoveList.TryGetMove(candidateId, out var candidateMove))
        {
            // Check stance reroutes (hitting kick while in the air -> jumpkick)
            if (candidateMove.StanceReroutes != null &&
                candidateMove.StanceReroutes.TryGetValue(StateTracker.CurrentStance, out MoveId rerouteId))
            {
                ChangeToMove(rerouteId);
                return;
            }

            ChangeToMove(candidateId);
        }
    }

    public override bool IsButtonHeld(ButtonName btn)
    {
        if (_gamepad == null || !_gamepad.IsConnected) return false;

        return btn switch
        {
            ButtonName.A => _gamepad.A().Pressed,
            ButtonName.B => _gamepad.B().Pressed,
            ButtonName.X => _gamepad.X().Pressed,
            ButtonName.Y => _gamepad.Y().Pressed,
            ButtonName.DPadUp => _gamepad.DPadUp().Pressed,
            ButtonName.DPadDown => _gamepad.DPadDown().Pressed,
            ButtonName.DPadLeft => _gamepad.DPadLeft().Pressed,
            ButtonName.DPadRight => _gamepad.DPadRight().Pressed,
            ButtonName.RightBumper => _gamepad.RightBumper().Pressed,
            _ => false
        };
    }

    public Vector2 GetMovementInput()
    {
        if (_gamepad == null) return Vector2.Zero;

        return new Vector2(
            _gamepad.DPadLeft().Pressed ? -1 : _gamepad.DPadRight().Pressed ? 1 : 0,
            _gamepad.DPadUp().Pressed ? 1 : _gamepad.DPadDown().Pressed ? -1 : 0
        );
    }

    public override void UpdatePhysics(float dt)
    {
        var movementDir = GetMovementInput();

        if (movementDir.X != 0)
        {
            Player.Flipped = movementDir.X < 0;
        }

        if (CurrentMove.Id == MoveId.Idle || CurrentMove.Id == MoveId.Run || CurrentMove.Id == MoveId.Jump)
        {
            if (movementDir.X != 0)
            {
                var targetVelocity = movementDir.X * PlayerConfig.WALK_SPEED;
                var currentVelocityX = Player.PhysicsBody.Velocity.X;
                var velocityDiff = targetVelocity - currentVelocityX;

                Player.PhysicsBody.ApplyForce(new Vector2(velocityDiff * Player.PhysicsBody.Mass * 5f, 0));
            }
        }
    }
}