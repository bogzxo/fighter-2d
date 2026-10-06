using System;

using Fighter2D.Character;

namespace Fighter2D.Combat;

/// <summary>
/// Says out loud what every attack of the fight came to, for whoever wants to show it (the HUD calls out HIT, COUNTER HIT, WHIFF etc.)
/// The HitResolver reports in here, nothing else should.
/// </summary>
internal sealed class CombatLog
{
    /// <summary>
    /// Called for every attack once its hit came out, with who threw it, what became of it and how many hits the combo is at.
    /// </summary>
    public event Action<Player, HitResult, int>? Reported;

    /// <param name="comboCount">How many hits in a row the victim has eaten including this one, 0 for anything that didn't land.</param>
    public void Report(Player attacker, HitResult result, int comboCount = 0) => Reported?.Invoke(attacker, result, comboCount);
}
