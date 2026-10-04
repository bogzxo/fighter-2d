using System.Collections.Generic;
using System.Numerics;

using Fighter2D.Character.Controllers;
using Fighter2D.Logic.Moves;
using Fighter2D.Scenes;

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

    // Shared by both kicks until they get animations of their own
    public IEnumerator<uint> Kick()
    {
        // Total animation length is 5 frames
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Attacking;
        _controller.PlayAnimation("kick");
        _controller.Opponent.Controller.PrepareForHit();

        // Hit happens on frame 4 (index 3)
        yield return 3;
        _controller.TryHit();
        yield return 2;
    }

    public IEnumerator<uint> HitStun()
    {
        // The status and its timer are set by the controller, we only show it for as long as it lasts
        while (_controller.StateTracker.CurrentStatus == PlayerStatusType.Stunned)
        {
            // Hunched over (first frame of the landing) with a haze around the head
            _controller.PlayAnimation("hard_landing");
            FightScene.Effects.Haze(_player.HeadPosition);
            yield return 1;
        }
    }

    public IEnumerator<uint> JumpKick()
    {
        // Total animation length is 8 frames
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Attacking;
        _controller.PlayAnimation("jump_kick");
        _controller.Opponent.Controller.PrepareForHit();

        yield return 4;
        _controller.TryHit();
        yield return 4;
    }

    public IEnumerator<uint> Block()
    {
        // Total animation length is 7 frames
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Guarding;
        _controller.PlayAnimation("block");

        // The AnimationManager's modulo math (%) will safely loop the 7 frames
        while (_controller.IsHeld(InputFlags.RightBumper))
        {
            yield return 1;
        }
    }

    public IEnumerator<uint> Jump()
    {
        // Total animation length is 4 frames
        _controller.PlayAnimation("jump");

        _player.PhysicsBody.ApplyImpulse(new Vector2(0, PlayerConfig.JUMP_IMPULSE));
        FightScene.Effects.Dust(_player.FeetPosition, 0);
        _controller.StateTracker.ResetFallDuration();

        yield return 4;

        // This is a mid-routine interrupt allowance
        _controller.CanInterrupt = true;

        while (_controller.StateTracker.CurrentStance == Stance.Jumping)
            yield return 1;
    }

    public IEnumerator<uint> DodgeRoll()
    {
        // Total animation length is 9 frames
        _controller.StateTracker.CurrentStatus = PlayerStatusType.Invulnerable;
        _controller.PlayAnimation("roll");

        float direction = _player.Flipped ? -1.0f : 1.0f;
        _player.PhysicsBody.ApplyImpulse(new Vector2(direction * PlayerConfig.WALK_SPEED * PlayerConfig.DASH_MULTIPLIER, 0));

        // The roll leaves a trail of dust behind it for as long as it is at speed
        for (int frame = 0; frame < 5; frame++)
        {
            FightScene.Effects.Dust(_player.FeetPosition, -direction, 8);
            yield return 1;
        }

        // Allow the player to cancel out of the end of the roll early
        _controller.CanInterrupt = true;

        yield return 4;
    }

    public IEnumerator<uint> Run()
    {
        _controller.CanInterrupt = true;

        do
        {
            while (IsRunHeld())
            {
                // Length is 12 frames, letting go ends the loop on the spot
                _controller.PlayAnimation("run_loop");
                for (int frame = 0; frame < 12 && IsRunHeld(); frame++)
                {
                    yield return 1;
                }
            }

            // Length is 5 frames, picking the run back up cuts the stop short
            _controller.PlayAnimation("run_stop");
            for (int frame = 0; frame < 5 && !IsRunHeld(); frame++)
            {
                yield return 1;
            }
        } while (IsRunHeld());
    }

    private bool IsRunHeld() =>
        _controller.IsHeld(InputFlags.DPadLeft) || _controller.IsHeld(InputFlags.DPadRight);

    public IEnumerator<uint> Fall()
    {
        // Total animation length is 1 frame
        _controller.CanInterrupt = true;
        _controller.StateTracker.CurrentStance = Stance.Falling;

        while (_controller.StateTracker.FallDuration > 0)
        {
            _controller.PlayAnimation("fall");
            yield return 1;
        }

        // We update the stance change here so the controller knows we landed
        _controller.StateTracker.CurrentStance = Stance.Standing;
    }

    public IEnumerator<uint> Idle()
    {
        // Total animation length is 1 frame
        _controller.StateTracker.CurrentStance = Stance.Standing;
        _controller.CanInterrupt = true;

        while (true)
        {
            _controller.PlayAnimation("idle");
            yield return 1;
        }
    }

    public IEnumerator<uint> Crouch()
    {
        _controller.StateTracker.CurrentStance = Stance.Crouching;

        // crouch_start length is 3 frames
        _controller.PlayAnimation("crouch_start");
        yield return 3;

        // From here on we can kick, jump and roll out of the crouch
        _controller.CanInterrupt = true;

        // crouch_loop length is 1 frame
        while (_controller.IsHeld(InputFlags.DPadDown))
        {
            _controller.PlayAnimation("crouch_loop");
            yield return 1;
        }

        // We keep the stance change here so the physics engine knows we stood up
        _controller.StateTracker.CurrentStance = Stance.Standing;
    }
}