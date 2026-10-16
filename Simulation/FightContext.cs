namespace Fighter2D.Simulation;

/// <summary>
/// The fight that is on, for everything that is part of it. FightScene makes one as it sets up and hands it to its players
/// (<see cref="Player.Fight"/>), its HUD and its netcode, and everything under a player gets at it through its controller.
/// It used to be a static called Fight, which meant there could only ever be one fight in the process (no test, no replay,
/// no spectator view) and anything anywhere could poke at it. Now whoever has one was given it.
/// </summary>
internal sealed class FightContext
{
    // P1 is whoever sits at this machine, online that means P1 is always us and P2 is the other guy
    public IFighter PlayerOne = null!;
    public IFighter PlayerTwo = null!;

    // Blood, dust and everything else that flies around
    public IFightEffects Effects = null!;

    // What every attack came to, the HUD listens in on this
    public CombatLog CombatLog = new();

    // The map the fight is on, for whoever needs to find their way around it
    public IArena? Stage;

    // Runs the rounds and says when the players are allowed to move
    public IRoundClock? Round;

    // Only there for an online fight
    public IHitReporter? Network;

    /// <summary>
    /// Whether the players are held still right now, which is the case between rounds.
    /// </summary>
    public bool PlayersFrozen => Round is { PlayersFrozen: true };

    /// <summary>
    /// Helper method to get whoever a player is fighting against.
    /// </summary>
    public IFighter OpponentOf(IFighter player) => ReferenceEquals(player, PlayerTwo) ? PlayerOne : PlayerTwo;
}
