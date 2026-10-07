using System;
using System.Collections.Generic;

namespace Fighter2D.Match;

internal enum RoundOutcome
{
    PlayerOne,
    PlayerTwo,

    // Double K.O., or the time ran out with both on the same health
    Draw
}

/// <summary>
/// Who won which round of a match, and with that who is ahead and whether it is over.
/// It only keeps count. When a round is over and who took it is decided by the RoundDirector.
/// </summary>
internal sealed class MatchScore(MatchRules rules)
{
    // How many drawn rounds are put up with on top of the ones the match is the best of, a match has to end at some point
    private const int EXTRA_ROUNDS = 2;

    private readonly List<RoundOutcome> _rounds = [];

    /// <summary>
    /// Who won the match because the other player left (0 or 1), null if nobody did.
    /// </summary>
    public int? ForfeitedTo { get; private set; }

    /// <summary>
    /// How every round that was played ended, the first one first.
    /// </summary>
    public IReadOnlyList<RoundOutcome> Rounds => _rounds;

    /// <summary>
    /// The round that is being played (or about to be), counting from 1.
    /// </summary>
    public int Round => _rounds.Count + 1;

    /// <summary>
    /// How many rounds a player has won, player 0 being player one.
    /// </summary>
    public int Wins(int player)
    {
        RoundOutcome theirs = player == 0 ? RoundOutcome.PlayerOne : RoundOutcome.PlayerTwo;

        int wins = 0;
        foreach (RoundOutcome round in _rounds)
        {
            if (round == theirs) wins++;
        }

        return wins;
    }

    /// <summary>
    /// Whether the match is decided. Somebody has enough rounds, somebody left, or it has dragged on for longer than it is allowed to.
    /// </summary>
    public bool IsOver =>
        ForfeitedTo is not null || Wins(0) >= rules.RoundsToWin || Wins(1) >= rules.RoundsToWin || _rounds.Count >= rules.Rounds + EXTRA_ROUNDS;

    /// <summary>
    /// Who won the match (0 or 1), null while it is still going and for a match that ended level.
    /// </summary>
    public int? Winner
    {
        get
        {
            if (!IsOver) return null;
            if (ForfeitedTo is int stayed) return stayed;

            int one = Wins(0), two = Wins(1);
            return one == two ? null : one > two ? 0 : 1;
        }
    }

    public void Record(RoundOutcome outcome) => _rounds.Add(outcome);

    /// <summary>
    /// Called when a player leaves in the middle of the match, which goes to whoever is still there.
    /// </summary>
    public void Forfeit(int winner) => ForfeitedTo = Math.Clamp(winner, 0, 1);

    /// <summary>
    /// Helper method to set the score to what the host says it is, for the machine that only follows the match.
    /// </summary>
    /// <param name="forfeitedTo">Who won by the other player leaving, anything below zero for nobody.</param>
    public void Restore(ReadOnlySpan<RoundOutcome> rounds, int forfeitedTo)
    {
        _rounds.Clear();
        _rounds.AddRange(rounds);

        ForfeitedTo = forfeitedTo < 0 ? null : Math.Clamp(forfeitedTo, 0, 1);
    }
}
