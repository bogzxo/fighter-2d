using Fighter2D.Character.Controllers;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Scenes;
using Horizon.Core;
using Horizon.Core.Collections;
using Horizon.Engine;
using Silk.NET.Input;
using System.Linq;
using System.Numerics;

namespace Fighter2D.Character.Controllers;

internal class GamepadPlayerController : PlayerController
{
    private const int MaxInputHistory = 16;
    private CircularBuffer<InputFlags> _circularInputFlags = new(MaxInputHistory);
    
    // @spd investigate if substepping the input buffer is a good idea
    private IntervalRunnerSubStep runnerFixedStep;

    public bool IsConnected => _gamepad != null && _gamepad.IsConnected;
    private readonly IGamepad? _gamepad;

    public GamepadPlayerController(int index, MoveList moveList) : base(moveList)
    {
        _gamepad = GameEngine.Instance.InputManager.NativeInputContext?.Gamepads.ElementAtOrDefault(index);
        runnerFixedStep = new(1 / 60.0f, FixedUpdate);
    }

    private void FixedUpdate()
    {
        // (bool left, bool up, bool down, bool right, bool a_button, bool b_button, bool x_button, bool y_button

        // save current state of the player input
        InputFlags current = (
            (_gamepad?.DPadLeft().Pressed ?? false ? InputFlags.DPadLeft : InputFlags.None) |
            (_gamepad?.DPadRight().Pressed ?? false ? InputFlags.DPadRight : InputFlags.None) |
            (_gamepad?.DPadUp().Pressed ?? false ? InputFlags.DPadUp : InputFlags.None) |
            (_gamepad?.DPadDown().Pressed ?? false ? InputFlags.DPadDown : InputFlags.None) |
            ((_gamepad?.A().Pressed ?? false) ? InputFlags.A : InputFlags.None) |
            ((_gamepad?.B().Pressed ?? false) ? InputFlags.B : InputFlags.None) |
            ((_gamepad?.X().Pressed ?? false) ? InputFlags.X : InputFlags.None) |
            ((_gamepad?.Y().Pressed ?? false) ? InputFlags.Y : InputFlags.None)
        );

        _circularInputFlags.Append(current);

        // squash into just the diffs for that whole array
        List<InputFlags> inputStates = [];
        foreach (var inputState in _circularInputFlags.ToArray())
        {
            if (!inputStates.Contains(inputState))
                inputStates.Add(inputState);
        }

        // match it to a move in an order of priority
        foreach (var (name, move) in MoveList.AllMoves)
        {
            if (move.InputSignature == InputFlags.None) continue;
            foreach (var inputState in inputStates
                         .Where(inputState => (move.InputSignature ^ inputState) == InputFlags.None))
            {
                /* @spd
                 * This returns very many matches as it is decoupled from TryProcessNewInputs, i believe that you should
                 * see how IntervalRunnerFixedStep or IntervalRunnerSubStep are implemented and copy that logic into the
                 * TryProcessNewInputs method and move your code there, that way your code doesnt override the logic handling
                 * when the player is stunned or combo trapped etc, but i also tagged you in PlayerController for a potential
                 * logic error regarding how being trapped into doesnt factor in logic for parrying, but thats a problem for later
                 */
                Console.WriteLine(name);
            }
        }
    }

    public override void TryProcessNewInputs(float dt)
    {
        // @spd

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
            if (StateTracker.IsGrounded)
            {
                ChangeToMove(MoveId.Run);
            }
        }
        else
        {
            TryAbortCurrentMove();
        }
    }

    private void TryAbortCurrentMove(MoveId newMove=MoveId.Idle)
    {
        // Ignore if we cant abort this move #prolife
        if (!CurrentMove.Interruptible) return;

        // Stance rerouting is handled in PlayerController
        if (CurrentMove.StanceReroutes != null &&
            CurrentMove.StanceReroutes.ContainsKey(StateTracker.CurrentStance)) return;

        // Only when we can abort (or change the move)
        ChangeToMove(newMove);
    }

    private void AttemptMove(MoveId candidateId)
    {
        // TODO: IMPORTANT BITCH we need to update this method to test for reroutes first, hence the attempt part of the name!
        // TODO: so that it can: AttemptMove(MoveId.Kick) but we're jumping, hence we do jumpkick!

        // Test if the move exists (future proofed for HIDL)
        if (!MoveList.TryGetMove(candidateId, out var candidateMove)) return;

        // Check stance reroutes (hitting kick while in the air -> jumpkick)
        if (candidateMove.StanceReroutes != null &&
            candidateMove.StanceReroutes.TryGetValue(StateTracker.CurrentStance, out MoveId rerouteId))
        {
            ChangeToMove(rerouteId);
            return;
        }

        ChangeToMove(candidateId);
    }

    public override bool IsButtonHeld(ButtonName btn)
    {
        if (_gamepad is not { IsConnected: true }) return false;

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

    // Here we can use ImGUI to show debug info
    public override void Render(float dt, object? obj = null)
    {

    }

    public override void UpdatePhysics(float dt)
    {
        runnerFixedStep.UpdateState(dt);

        var movementDir = GetMovementInput();
        if (movementDir.X != 0)
        {
            Player.Flipped = movementDir.X < 0;
        }

        // rudementary temporary move logic
        if (CurrentMove.Id is MoveId.Idle or MoveId.Run && StateTracker.CurrentStance == Stance.Standing)
        {
            if (movementDir.X == 0) return;

            var targetVelocity = movementDir.X * PlayerConfig.WALK_SPEED;
            var currentVelocityX = Player.PhysicsBody.Velocity.X;
            var velocityDiff = targetVelocity - currentVelocityX;

            Player.PhysicsBody.ApplyForce(new Vector2(velocityDiff * Player.PhysicsBody.Mass * 5f, 0));
        }
    }
}