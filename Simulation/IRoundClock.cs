namespace Fighter2D.Simulation;

/// <summary>
/// What the simulation wants to know of the rounds, whether the players are held still and where the match is. The
/// <see cref="RoundDirector"/> is one, a test has none and the fight is simply on bruh.
/// </summary>
internal interface IRoundClock
{
    /// <summary>Whether the players are held still, which is before a round and once it has been called.</summary>
    bool PlayersFrozen { get; }

    RoundPhase Phase { get; }
}
