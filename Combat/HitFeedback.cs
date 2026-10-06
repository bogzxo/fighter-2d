using System.Numerics;

using Fighter2D.Character.Controllers;
using Fighter2D.Logic.Moves;

using Horizon.Rendering.Spriting;

namespace Fighter2D.Combat;

/// <summary>
/// Makes a hit feel like one. Hitstop on both players, a flash on whoever took it and the blood or sparks that go with it.
/// None of this changes the fight, the damage and the hitstun are the HitResolver's business.
/// </summary>
internal static class HitFeedback
{
    // How long (in seconds) the victim stays lit up
    private const float FLASH_TIME = 0.22f;
    private const float COUNTER_FLASH_TIME = 0.4f;
    private const float KO_FLASH_TIME = 0.6f;

    // Tints above 1 draw the sprite brighter than it is, which reads as a white flash
    private static readonly Vector4 HitFlash = new(2.6f, 2.4f, 2.4f, 1.0f);
    private static readonly Vector4 CounterFlash = new(3.2f, 2.2f, 0.9f, 1.0f);
    private static readonly Vector4 BlockFlash = new(1.4f, 1.6f, 2.0f, 1.0f);
    private static readonly Vector4 KoFlash = new(3.6f, 3.4f, 3.4f, 1.0f);

    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    /// <param name="lethal">Whether this was the hit that took the last of their health.</param>
    public static void Play(PlayerController attacker, PlayerController victim, FightingMove move, HitResult result, float direction, Vector2 impact, bool lethal)
    {
        if (result == HitResult.Whiff) return;

        if (result == HitResult.Blocked)
        {
            Fight.Effects.Block(impact, direction);
            Freeze(attacker, victim, CombatRules.BLOCK_HITSTOP);
            Flash(victim, BlockFlash, FLASH_TIME);
            return;
        }

        // The knockout gets the full treatment, both of them hang there for a moment so everybody sees who just lost
        if (lethal)
        {
            Fight.Effects.Hit(impact, direction, heavy: true);
            Freeze(attacker, victim, CombatRules.KO_HITSTOP);
            Flash(victim, KoFlash, KO_FLASH_TIME);
            return;
        }

        if (result == HitResult.CounterHit)
        {
            Fight.Effects.Hit(impact, direction, heavy: true);
            Freeze(attacker, victim, CombatRules.COUNTER_HITSTOP);
            Flash(victim, CounterFlash, COUNTER_FLASH_TIME);
            return;
        }

        // A plain hit and a punish feel the same, the punish only gets its name called out
        Fight.Effects.Hit(impact, direction, heavy: move.HasKnockback);
        Freeze(attacker, victim, move.HasKnockback ? CombatRules.HEAVY_HITSTOP : CombatRules.HITSTOP);
        Flash(victim, HitFlash, FLASH_TIME);
    }

    private static void Freeze(PlayerController attacker, PlayerController victim, int ticks)
    {
        attacker.ApplyHitstop(ticks);
        victim.ApplyHitstop(ticks);
    }

    private static void Flash(PlayerController victim, Vector4 colour, float time)
    {
        victim.Player.Tint = colour;
        victim.Player.TweenTint(Vector4.One, time);
    }
}
