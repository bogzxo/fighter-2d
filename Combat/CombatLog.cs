using System;

namespace Fighter2D.Combat;

/// <summary>
/// What one attack came to once its hit came out.
/// </summary>
/// <param name="ComboCount">How many hits in a row the victim has eaten including this one, 0 for anything that didn't land.</param>
/// <param name="ComboDamage">How much damage those hits did all together.</param>
internal readonly record struct AttackReport(Player Attacker, FightingMove Move, HitResult Result, int ComboCount = 0, int ComboDamage = 0);

/// <summary>
/// Says out loud what every attack of the fight came to, for whoever wants to show it (the HUD calls out HIT, COUNTER HIT, WHIFF etc.)
/// The HitResolver reports in here, and so does the netcode for the attacks that were decided on the other machine.
/// </summary>
internal sealed class CombatLog
{
    /// <summary>
    /// Called for every attack once its hit came out.
    /// </summary>
    public event Action<AttackReport>? Reported;

    public void Report(in AttackReport report) => Reported?.Invoke(report);
}
