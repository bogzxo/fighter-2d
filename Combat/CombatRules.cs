namespace Fighter2D.Combat;

/// <summary>
/// Every number that decides what a hit is worth. All times are in ticks of the fight, see PlayerConfig.TICK_RATE.
/// This is the file to mess with when the game feels off.
/// </summary>
internal static class CombatRules
{
    /* Hitstun for moves that don't say their own (see MoveFrameData).
       A move starts out BASE_DISADVANTAGE ticks minus on hit and earns STARTUP_REWARD ticks back for every tick of startup.
       So jabs are a bit minus and the victim can mash out of them with something fast.
       Slow moves end up plus, which is the window to link a quick one after them. That is a combo. */
    public const float BASE_DISADVANTAGE = 6.0f;
    public const float STARTUP_REWARD = 0.75f;
    public const float MAX_ADVANTAGE = 24.0f;
    public const int MIN_HITSTUN = 4;

    // A counter hit catches somebody in the startup of their own attack, which is on them
    public const float COUNTER_HITSTUN_SCALE = 1.5f;
    public const float COUNTER_DAMAGE_SCALE = 1.25f;

    // How much hitstun a juggle hit adds on top of what the victim has left. Every hit adds less so nobody stays up there forever
    public const int JUGGLE_EXTENSION = 10;
    public const int JUGGLE_DECAY = 2;
    public const int MIN_JUGGLE_EXTENSION = 2;

    // How long both players freeze on the frame a hit landed
    public const int HITSTOP = 4;
    public const int HEAVY_HITSTOP = 8;
    public const int COUNTER_HITSTOP = 11;
    public const int BLOCK_HITSTOP = 2;
}
