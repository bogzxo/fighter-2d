using System;

namespace Fighter2D.Moves;

/// <summary>
/// The frame data of a move, in ticks of the fight (see FightTicks.TICK_RATE) and not in frames of its animation.
/// Characters animate at different rates so animation frames can't be compared between them, ticks can.
/// </summary>
/// <param name="Startup">How long after the move starts its hit comes out, 0 for a move that doesn't hit.</param>
/// <param name="Recovery">How long the move goes on for after the hit came out.</param>
/// <param name="Hitstun">How long whoever it lands on is in hitstun for.</param>
/// <param name="Blockstun">How long whoever blocks it is stuck in their block for.</param>
public readonly record struct MoveFrameData(int Startup, int Recovery, int Hitstun, int Blockstun)
{
    /// <summary>
    /// Frame advantage on hit. Plus means the attacker can act first, minus means whoever got hit can.
    /// </summary>
    public int OnHit => Hitstun - Recovery;

    /// <summary>
    /// Frame advantage on block. Nearly always minus, which is what makes a blocked move a punish.
    /// </summary>
    public int OnBlock => Blockstun - Recovery;

    /// <summary>
    /// Helper method to work out the frame data of a move.
    /// </summary>
    /// <param name="animationLength">How many frames an animation of the character has.</param>
    /// <param name="frameRate">How many frames of animation a second the character plays at.</param>
    public static MoveFrameData Of(FightingMove move, Func<string?, uint> animationLength, float frameRate)
    {
        float ticksPerFrame = FightTicks.TICK_RATE / MathF.Max(1.0f, frameRate);

        // A phase that loops counts once, nobody knows how often it comes round until it is played
        uint total = 0;
        int hitFrame = -1;

        foreach (MovePhase phase in move.Phases)
        {
            uint frames = phase.LengthFor(animationLength(phase.Animation));

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
            : recovery + MathF.Min(CombatRules.MAX_ADVANTAGE, startup * CombatRules.STARTUP_REWARD - CombatRules.BASE_DISADVANTAGE)
                + move.Damage * CombatRules.HITSTUN_PER_DAMAGE;
        hitstun = MathF.Max(CombatRules.MIN_HITSTUN, hitstun);

        // The same for the blockstun, which is a share of the hitstun unless the move says. Short enough that a blocked move
        // is a punish, long enough that mashing out of a block isn't free
        float blockstun = move.Blockstun >= 0
            ? move.Blockstun * ticksPerFrame
            : hitstun * CombatRules.BLOCKSTUN_SCALE;

        return new MoveFrameData(
            (int)MathF.Round(startup),
            (int)MathF.Round(recovery),
            (int)MathF.Round(hitstun),
            Math.Max(CombatRules.MIN_BLOCKSTUN, (int)MathF.Round(blockstun)));
    }
}
