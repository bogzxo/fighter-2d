using System;
using System.Collections.Generic;
using System.Numerics;

using Fighter2D.Character.Controllers;
using Fighter2D.Logic.Moves;
using Fighter2D.Scenes;

namespace Fighter2D.Logic.Routines;

/// <summary>
/// Plays a move frame by frame the way its file describes it: one phase after the other, each with its animation, the frame it hits on,
/// the frame it can be cut short from and what it waits for. There is one of these for every move, whatever the move is.
/// </summary>
internal static class MoveRoutine
{
    // The effects a phase can ask for by name
    private const string EFFECT_JUMP = "jump";
    private const string EFFECT_ROLL = "roll";
    private const string EFFECT_HAZE = "haze";

    /// <summary>
    /// The routine of a move, every step of it is one frame of animation.
    /// </summary>
    public static IEnumerator<uint> Run(FightingMove move, PlayerController controller)
    {
        var player = controller.Player;
        var state = controller.StateTracker;

        if (move.Status is { } status) state.CurrentStatus = status;
        if (move.Stance is { } stance) state.CurrentStance = stance;
        if (move.Warns) controller.Opponent.Controller.PrepareForHit();

        do
        {
            bool tookTime = false;

            foreach (MovePhase phase in move.Phases)
            {
                // A phase that waits on something which isn't the case never starts (letting go of block before it is up)
                if (phase.While != MoveCondition.None && !Test(phase.While, move, controller)) continue;

                if (phase.Impulse != Vector2.Zero)
                {
                    // X is the way we are facing
                    float direction = player.Flipped ? -1.0f : 1.0f;
                    player.PhysicsBody.ApplyImpulse(new Vector2(direction * phase.Impulse.X, phase.Impulse.Y));

                    // Going up is the start of a new fall
                    if (phase.Impulse.Y > 0) state.ResetFallDuration();
                }

                bool ended = false;
                uint effectFrame = 0;

                do
                {
                    if (phase.Animation is not null) controller.PlayAnimation(phase.Animation);

                    uint frames = phase.Frames > 0 ? phase.Frames : controller.GetAnimationLength(phase.Animation);

                    for (uint frame = 0; frame < frames; frame++)
                    {
                        if (phase.While != MoveCondition.None && !Test(phase.While, move, controller)) { ended = true; break; }
                        if (phase.Until != MoveCondition.None && Test(phase.Until, move, controller)) { ended = true; break; }

                        if (frame == phase.Interrupt) controller.CanInterrupt = true;
                        if (frame == phase.Lock) controller.CanInterrupt = false;
                        if (phase.Status is { } phaseStatus && frame == phase.StatusFrame) state.CurrentStatus = phaseStatus;
                        if (frame == phase.Hit) controller.TryHit(frame);

                        if (phase.Effect is not null && (phase.EffectFrames == 0 || effectFrame < phase.EffectFrames))
                        {
                            PlayEffect(phase.Effect, controller);
                        }
                        effectFrame++;

                        tookTime = true;
                        yield return 1;
                    }
                } while (!ended && phase.While != MoveCondition.None);
            }

            // A move that starts over without any of its phases running would never let go of the frame
            if (!tookTime) yield return 1;
        } while (move.Repeat != MoveCondition.None && Test(move.Repeat, move, controller));

        // We update the stance change here so the controller knows the move let go of it (landed, stood up)
        if (move.StanceAfter is { } after) state.CurrentStance = after;
    }

    private static bool Test(MoveCondition condition, FightingMove move, PlayerController controller) => condition switch
    {
        MoveCondition.Held => IsHeld(move, controller),
        MoveCondition.Stunned => controller.StateTracker.CurrentStatus == PlayerStatusType.Stunned,
        MoveCondition.Jumping => controller.StateTracker.CurrentStance == Stance.Jumping,
        MoveCondition.Falling => controller.StateTracker.FallDuration > 0,
        MoveCondition.Always => true,
        _ => false
    };

    /// <summary>
    /// Helper method to test if any of the inputs that start the move is (still) held down.
    /// </summary>
    private static bool IsHeld(FightingMove move, PlayerController controller)
    {
        foreach (InputFlags signature in move.InputSignatures)
        {
            if (signature != InputFlags.None && controller.IsHeld(signature)) return true;
        }

        return false;
    }

    private static void PlayEffect(string effect, PlayerController controller)
    {
        var player = controller.Player;
        float direction = player.Flipped ? -1.0f : 1.0f;

        switch (effect)
        {
            case EFFECT_JUMP:
                FightScene.Effects.Dust(player.FeetPosition, 0);
                FightScene.Effects.Shockwave(player.FeetPosition, 45, 110);
                break;

            case EFFECT_ROLL:
                // A trail of dust behind us. Every frame of it, so what stays underfoot gets all of them: each one has to be gentle
                FightScene.Effects.Dust(player.FeetPosition, -direction, 8);
                FightScene.Effects.Shockwave(player.FeetPosition, 70, 45);
                break;

            case EFFECT_HAZE:
                FightScene.Effects.Haze(player.HeadPosition);
                break;
        }
    }

    /// <summary>
    /// Whether a phase can ask for an effect by this name, for telling whoever wrote the file when it can't.
    /// </summary>
    public static bool IsEffect(string effect) => effect is EFFECT_JUMP or EFFECT_ROLL or EFFECT_HAZE;
}
