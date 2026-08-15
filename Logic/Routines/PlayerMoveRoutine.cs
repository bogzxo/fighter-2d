using System.Collections.Generic;
using System.Numerics;

using Fighter2D.Character.Controllers;
using Fighter2D.Logic.Moves;

using Silk.NET.Input;

namespace Fighter2D.Logic.Routines;

internal class PlayerMoveRoutines
{
    private readonly Character.Player _player;
    private readonly PlayerController _controller;

    public PlayerMoveRoutines(Character.Player player, PlayerController controller)
    {
        _player = player;
        _controller = controller;
    }

    public IEnumerator<int> KickLeft()
    {
        // Total animation length is 5 frames
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Attacking;
        _controller.PlayAnimation("kick");

        // Hit happens on frame 3
        yield return 2;
        // Active Hitbox here

        // Continue
        yield return 2;

        _controller.ChangeToMove(MoveId.Idle);
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Normal;
        _controller.CanInterrupt = true;

    }

    public IEnumerator<int> JumpKick()
    {
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Attacking;
        _controller.PlayAnimation("jump_kick");

        yield return 7;

        _controller.StateTracker.CurrentStatus = PlayerStatusType.Normal;
        _controller.CanInterrupt = true;
    }

    public IEnumerator<int> Block()
    {
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Guarding;
        _controller.PlayAnimation("block");

        while (_controller.IsButtonHeld(ButtonName.RightBumper))
        {
            yield return 1;
        }

        _controller.ChangeToMove(MoveId.Idle);
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Normal;
        _controller.CanInterrupt = true;
    }

    public IEnumerator<int> Jump()
    {
        _controller.PlayAnimation("jump");

        _player.PhysicsBody.ApplyImpulse(new Vector2(0, PlayerConfig.JUMP_IMPULSE));
        _controller.StateTracker.ResetFallDuration();
        
        yield return 3;
        _controller.CanInterrupt = true;

        while (_controller.StateTracker.CurrentStance == Stance.Jumping)
            yield return 0;
    }

    public IEnumerator<int> DodgeRoll()
    {
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Invulnerable;
        _controller.PlayAnimation("roll");

        float direction = _player.Flipped ? -1.0f : 1.0f;
        _player.PhysicsBody.ApplyImpulse(new Vector2(direction * PlayerConfig.WALK_SPEED * PlayerConfig.DASH_MULTIPLIER, 0));

        yield return 4;
        _controller.CanInterrupt = true;
        yield return 4;

        _controller.ChangeToMove(MoveId.Idle);
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Normal;
    }

    public IEnumerator<int> Run()
    {
        // todo: sme way to find the animation lengths

        _controller.CanInterrupt = true;
        while (_controller.IsButtonHeld(ButtonName.DPadLeft) || _controller.IsButtonHeld(ButtonName.DPadRight))
        {
            _controller.PlayAnimation("run_loop");
            yield return 11;
        }


        _controller.PlayAnimation("run_stop");
        yield return 4;

        _controller.ChangeToMove(MoveId.Idle);
    }
    public IEnumerator<int> Fall()
    {
        _controller.CanInterrupt = true;
        _controller.StateTracker.CurrentStance = Stance.Falling;

        _controller.PlayAnimation("fall");
        yield return 0;

        while (_controller.StateTracker.FallDuration > 0)
        {
            _controller.PlayAnimation("fall");
            yield return 0;
        }

        _controller.StateTracker.CurrentStance = Stance.Standing;
        _controller.ChangeToMove(MoveId.Idle);
    }
    public IEnumerator<int> Idle()
    {
        _controller.CanInterrupt = true;
        yield return 0;
        _controller.StateTracker.CurrentStance = Stance.Standing;
    }

    public IEnumerator<int> Crouch()
    {
        _controller.StateTracker.CurrentStance = Stance.Crouching;

        _controller.PlayAnimation("crouch_start");
        yield return 2;
        _controller.CanInterrupt = true;

        while (_controller.IsButtonHeld(ButtonName.DPadDown))
        {
            _controller.CanInterrupt = false;
            _controller.PlayAnimation("crouch_loop");
            yield return 0;
        }

        _controller.CanInterrupt = true;
        _controller.StateTracker.CurrentStance = Stance.Standing;
        _controller.ChangeToMove(MoveId.Idle);
    }
}