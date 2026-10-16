using System.Collections.Generic;

namespace Fighter2D.Match;

/// <summary>
/// How a round that was called ended, for the result card.
/// </summary>
internal enum RoundEnding
{
    KO,
    // Won without getting touched
    Perfect,
    // The clock ran out with one of them ahead
    TimeUp,
    DoubleKO,
    // The clock ran out with both on the same health
    Draw
}

/// <summary>
/// One round that was called, who took it (null for nobody) and how.
/// </summary>
internal readonly record struct RoundResult(int? Winner, RoundEnding Ending);

/// <summary>
/// What one player did over a match. Their attacks by what became of them, the damage they did and the longest combo
/// they got in.
/// </summary>
internal sealed class FighterStats
{
    public int Hits, CounterHits, Punishes, Blocked, Parried, Whiffs;
    public int Damage, LongestCombo;
}

/// <summary>
/// Everything the result card shows, counted as the match goes. It listens to the combat log for every attack and to the
/// rounds for how each one ended, and keeps nothing else. A fight that is built again (the data reloaded, another
/// dummy) starts its own, what came before is gone with the scene.
/// </summary>
internal sealed class MatchStats
{
    private readonly FightContext _fight;
    private readonly RoundDirector _round;
    private readonly FighterStats[] _players = [new(), new()];
    private readonly List<RoundResult> _rounds = [];

    // What the combo each of them is eating had done before the hit being counted, so a hit's own damage is the step up
    private readonly int[] _comboDamage = new int[2];

    public MatchStats(FightContext fight, RoundDirector round)
    {
        _fight = fight;
        _round = round;
        fight.CombatLog.Reported += Count;
        round.PhaseChanged += OnPhaseChanged;
    }

    /// <summary>What a player did, 0 being player one.</summary>
    public FighterStats Of(int player) => _players[player];

    /// <summary>Every round that was called, the first first.</summary>
    public IReadOnlyList<RoundResult> Rounds => _rounds;

    private void Count(AttackReport report)
    {
        int attacker = ReferenceEquals(report.Attacker, _fight.PlayerOne) ? 0 : 1, victim = 1 - attacker;
        FighterStats stats = _players[attacker];

        switch (report.Result)
        {
            case HitResult.Whiff: stats.Whiffs++; return;
            case HitResult.Blocked: stats.Blocked++; return;
            case HitResult.Parried: stats.Parried++; return;
            case HitResult.CounterHit: stats.CounterHits++; break;
            case HitResult.Punish: stats.Punishes++; break;
        }

        stats.Hits++;

        // The report says what the whole combo has come to, the first hit of one starts from nothing
        int before = report.ComboCount > 1 ? _comboDamage[victim] : 0;
        stats.Damage += System.Math.Max(0, report.ComboDamage - before);
        _comboDamage[victim] = report.ComboDamage;

        if (report.ComboCount > stats.LongestCombo) stats.LongestCombo = report.ComboCount;
    }

    private void OnPhaseChanged(RoundPhase phase)
    {
        if (phase != RoundPhase.RoundOver || _round.LastOutcome is not { } outcome) return;

        // The players aren't put back until the next round starts, so their health is still what the round ended on
        _rounds.Add(outcome switch
        {
            RoundOutcome.PlayerOne => new RoundResult(0, EndingFor(0)),
            RoundOutcome.PlayerTwo => new RoundResult(1, EndingFor(1)),
            _ => new RoundResult(null, _round.TimedOut ? RoundEnding.Draw : RoundEnding.DoubleKO)
        });
    }

    private RoundEnding EndingFor(int winner)
    {
        if (_round.TimedOut) return RoundEnding.TimeUp;
        return _round.PlayerOf(winner).Health == CombatRules.MAX_HEALTH ? RoundEnding.Perfect : RoundEnding.KO;
    }
}
