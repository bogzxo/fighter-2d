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
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Attacking;
        _controller.PlayAnimation("kick");

        yield return 7;
        // Active Hitbox here
        yield return 10;

        _controller.ChangeToMove(MoveId.Idle);
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Normal;
    }

    public IEnumerator<int> JumpKick()
    {
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Attacking;
        _controller.PlayAnimation("jump_kick");

        yield return 5;
        yield return 12;

        _controller.StateTracker.CurrentStatus = PlayerStatusType.Normal;
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
    }

    public IEnumerator<int> Jump()
    {
        _controller.PlayAnimation("jump");

        _player.PhysicsBody.ApplyImpulse(new Vector2(0, PlayerConfig.JUMP_IMPULSE));
        _controller.StateTracker.ResetFallDuration();

        yield return 4;
    }

    public IEnumerator<int> DodgeRoll()
    {
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Invulnerable;
        _controller.PlayAnimation("roll");

        float direction = _player.Flipped ? -1.0f : 1.0f;
        _player.PhysicsBody.ApplyImpulse(new Vector2(direction * PlayerConfig.WALK_SPEED * PlayerConfig.DASH_MULTIPLIER, 0));

        yield return 15;

        _controller.ChangeToMove(MoveId.Idle);
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Normal;
    }

    public IEnumerator<int> Run()
    {
        //_controller.PlayAnimation("run_start");

        // todo: sme way to find the animation lengths
        yield return 4;


        _controller.PlayAnimation("run_loop");

        while (_controller.IsButtonHeld(ButtonName.DPadLeft) || _controller.IsButtonHeld(ButtonName.DPadRight))
        {
            yield return 1;
        }


        _controller.PlayAnimation("run_stop");


        yield return 4;

        _controller.ChangeToMove(MoveId.Idle);
    }

    public IEnumerator<int> Crouch()
    {
        _controller.StateTracker.CurrentStance = Stance.Crouching;

        _controller.PlayAnimation("crouch_start");
        yield return 3;

        _controller.PlayAnimation("crouch_loop");

        while (_controller.IsButtonHeld(ButtonName.DPadDown))
        {
            yield return 1;
        }

        _controller.StateTracker.CurrentStance = Stance.Standing;
        _controller.ChangeToMove(MoveId.Idle);
    }
}