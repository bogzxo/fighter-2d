using System;

namespace Fighter2D.Simulation;

/// <summary>
/// What one attack came to once its hit came out.
/// </summary>
/// <param name="ComboCount">How many hits in a row the victim has eaten including this one, 0 for anything that didn't land.</param>
/// <param name="ComboDamage">How much damage those hits did all together.</param>
internal readonly record struct AttackReport(IFighter Attacker, FightingMove Move, HitResult Result, int ComboCount = 0, int ComboDamage = 0);

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

    public void Report(in AttackReport report)
    {
        // FIGHTER2D_LOG_HITS=1 writes every attack into the log, for checking what a scripted fight came to
        if (LogHits)
        {
            IFighter attacker = report.Attacker, victim = attacker.Fight.OpponentOf(attacker);
            Box hit = attacker.Body.Hitbox(attacker.Controller.Playback.Shown), hurt = victim.Body.Hurtbox;
            Horizon.Logging.Log.Info($"[Hits] {attacker.Character.Id} {report.Move.Id} {report.Result} (combo {report.ComboCount}, {report.ComboDamage} damage), " +
                $"attacker at {attacker.Body.Position.X:0} facing {attacker.Body.Facing}, hitbox {hit.Min.X:0},{hit.Min.Y:0} to {hit.Max.X:0},{hit.Max.Y:0}, " +
                $"{victim.Character.Id} at {victim.Body.Position.X:0} {(victim.Controller.State.IsDowned ? "down" : "up")}, hurtbox {hurt.Min.X:0},{hurt.Min.Y:0} to {hurt.Max.X:0},{hurt.Max.Y:0}");
        }

        Reported?.Invoke(report);
    }

    private static readonly bool LogHits = Environment.GetEnvironmentVariable("FIGHTER2D_LOG_HITS") == "1";
}
