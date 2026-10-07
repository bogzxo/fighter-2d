using System;

using Fighter2D.Character.Controllers;
using Fighter2D.Combat;

namespace Fighter2D.Logic.Moves;

/// <summary>
/// The frame data of a move, in ticks of the fight (see PlayerConfig.TICK_RATE) and not in frames of its animation.
/// Characters animate at different rates so animation frames can't be compared between them, ticks can.
/// </summary>
/// <param name="Startup">How long after the move starts its hit comes out, 0 for a move that doesn't hit.</param>
/// <param name="Recovery">How long the move goes on for after the hit came out.</param>
/// <param name="Hitstun">How long whoever it lands on is in hitstun for.</param>
public readonly record struct MoveFrameData(int Startup, int Recovery, int Hitstun)
{
    /// <summary>
    /// Frame advantage on hit. Plus means the attacker can act first, minus means whoever got hit can.
    /// </summary>
    public int OnHit => Hitstun - Recovery;

    /// <summary>
    /// Helper method to work out the frame data of a move.
    /// </summary>
    /// <param name="animationLength">How many frames an animation of the character has.</param>
    /// <param name="frameRate">How many frames of animation a second the character plays at.</param>
    public static MoveFrameData Of(FightingMove move, Func<string?, uint> animationLength, float frameRate)
    {
        float ticksPerFrame = PlayerConfig.TICK_RATE / MathF.Max(1.0f, frameRate);

        // A phase that loops counts once, nobody knows how often it comes round until it is played
        uint total = 0;
        int hitFrame = -1;

        foreach (MovePhase phase in move.Phases)
        {
            uint frames = phase.Frames > 0 ? phase.Frames : Math.Max(1, animationLength(phase.Animation));

            if (hitFrame < 0 && phase.HitFrame >= 0) hitFrame = (int)total + phase.HitFrame;
            total += frames;
        }

        // Nothing to work out for a move that doesn't hit
        if (hitFrame < 0) return default;

        float startup = hitFrame * ticksPerFrame;
        float recovery = (total - hitFrame) * ticksPerFrame;

        // A move that says its hitstun is taken at its word, in frames of its own animation.
        // One that doesn't gets it from its startup, the longer the windup the more plus it is on hit
        float hitstun = move.Hitstun >= 0
            ? move.Hitstun * ticksPerFrame
            : recovery + MathF.Min(CombatRules.MAX_ADVANTAGE, startup * CombatRules.STARTUP_REWARD - CombatRules.BASE_DISADVANTAGE);

        return new MoveFrameData(
            (int)MathF.Round(startup),
            (int)MathF.Round(recovery),
            Math.Max(CombatRules.MIN_HITSTUN, (int)MathF.Round(hitstun)));
    }
}
