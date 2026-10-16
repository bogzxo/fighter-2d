namespace Fighter2D.Match;

/// <summary>
/// The steps a match goes through, see <see cref="RoundDirector"/>.
/// </summary>
internal enum RoundPhase
{
    // The two fighters are shown off against each other, once before the first round
    Versus,

    // The round is called out, nobody can move yet
    Ready,

    // The round is on
    Fight,

    // Somebody went down or the time ran out, the round is being called
    RoundOver,

    // The match is decided, the winner is shown until the players have seen enough
    MatchOver
}
