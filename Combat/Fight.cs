
namespace Fighter2D.Combat;

/// <summary>
/// The fight that is on right now. Anything that needs to poke at the fight asks here instead of dragging the whole scene around.
/// FightScene fills this in when it sets up and empties it when it leaves.
/// </summary>
internal static class Fight
{
    // P1 is whoever sits at this machine, online that means P1 is always us and P2 is the other guy
    public static Player PlayerOne = null!;
    public static Player PlayerTwo = null!;

    // Blood, dust and everything else that flies around
    public static FightEffects Effects = null!;

    // What every attack came to, the HUD listens in on this
    public static CombatLog CombatLog = new();

    // The map the fight is on, for whoever needs to find their way around it
    public static FightingStage? Stage;

    // Runs the rounds and says when the players are allowed to move
    public static RoundDirector? Round;

    // Only there for an online fight
    public static FightNetwork? Network;

    /// <summary>
    /// Whether the players are held still right now, which is the case between rounds.
    /// </summary>
    public static bool PlayersFrozen => Round is { PlayersFrozen: true };

    /// <summary>
    /// Helper method to get whoever a player is fighting against.
    /// </summary>
    public static Player OpponentOf(Player player) => player == PlayerTwo ? PlayerOne : PlayerTwo;

    /// <summary>
    /// Called when the fight is over and the scene packs up.
    /// </summary>
    public static void Clear()
    {
        Round = null;
        Network = null;
        Stage = null;
    }
}
