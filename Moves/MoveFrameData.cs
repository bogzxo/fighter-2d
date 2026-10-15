using System;

namespace Fighter2D.Moves;

/// <summary>
/// The frame data of a move, in frames of the fight (see FightTicks.TICK_RATE), the same frames the move file is written in.
/// </summary>
/// <param name="Startup">How long after the move starts its hit comes out, 0 for a move that doesn't hit.</param>
/// <param name="Recovery">How long the move goes on for after its last active frame, see <see cref="FightingMove.ActiveFrames"/>.</param>
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
        // A phase that loops counts once, nobody knows how often it comes round until it is played
        int total = 0;
        int hitFrame = -1;

        foreach (MovePhase phase in move.Phases)
        {
            int frames = (int)phase.LengthFor(animationLength(phase.Animation), frameRate);

            if (hitFrame < 0 && phase.HitFrame >= 0) hitFrame = total + phase.HitFrame;
            total += frames;
        }

        // Nothing to work out for a move that doesn't hit
        if (hitFrame < 0) return default;

        // Startup, then the active frames the hitbox is out for, then recovery, which is everything after the last of them
        int active = Math.Max(1, move.ActiveFrames);
        int startup = hitFrame;
        int recovery = Math.Max(1, total - hitFrame - (active - 1));

        // A move that says its hitstun gets exactly that. One that doesn't gets a guess out of its startup, the longer the
        // windup the more plus it is on hit, which Hex shows so it can be written in
        int hitstun = move.Hitstun >= 0
            ? move.Hitstun
            : (int)MathF.Round(MathF.Max(CombatRules.GUESSED_MIN_HITSTUN,
                recovery + MathF.Min(CombatRules.MAX_ADVANTAGE, startup * CombatRules.STARTUP_REWARD - CombatRules.BASE_DISADVANTAGE)
                + move.Damage * CombatRules.HITSTUN_PER_DAMAGE));

        // The same for the blockstun, which is a share of the hitstun unless the move says. Short enough that a blocked move
        // is a punish, long enough that mashing out of a block isn't free
        int blockstun = move.Blockstun >= 0
            ? move.Blockstun
            : Math.Max(CombatRules.GUESSED_MIN_BLOCKSTUN, (int)MathF.Round(hitstun * CombatRules.BLOCKSTUN_SCALE));

        return new MoveFrameData(startup, recovery, hitstun, blockstun);
    }
}
