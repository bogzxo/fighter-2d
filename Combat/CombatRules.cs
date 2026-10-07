namespace Fighter2D.Combat;

/// <summary>
/// Every number that decides what a hit is worth. All times are in ticks of the fight, see FightTicks.TICK_RATE.
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

    /* Blocking. A blocked hit still costs something: the blocker is stuck in their block for a share of the hitstun,
       gets shoved back, and takes a sliver of the damage as chip. Chip never finishes anybody off */
    public const float BLOCKSTUN_SCALE = 0.7f;
    public const int MIN_BLOCKSTUN = 3;
    public const float CHIP_DAMAGE_SCALE = 0.12f;
    public const float BLOCK_PUSHBACK = 420.0f;

    // A hit that doesn't launch still shoves the victim back a step, so a flurry of jabs walks them across the stage
    public const float HIT_PUSHBACK = 300.0f;

    /* Combos. Every hit after the first does less, so a long combo isn't a round by itself */
    public const float COMBO_DAMAGE_SCALE = 0.88f;
    public const float MIN_COMBO_DAMAGE_SCALE = 0.35f;

    // A counter hit catches somebody in the startup of their own attack, which is on them
    public const float COUNTER_HITSTUN_SCALE = 1.5f;
    public const float COUNTER_DAMAGE_SCALE = 1.25f;

    // How much hitstun a juggle hit adds on top of what the victim has left. Every hit adds less so nobody stays up there forever
    public const int JUGGLE_EXTENSION = 10;
    public const int JUGGLE_DECAY = 2;
    public const int MIN_JUGGLE_EXTENSION = 2;

    /* Knockdowns. How long somebody lies on the floor before they get up, during which nothing can hit them.
       Getting up has i-frames of its own, those are the get_up move's business */
    public const int KNOCKDOWN_TICKS = 40;

    // How long both players freeze on the frame a hit landed
    public const int HITSTOP = 4;
    public const int HEAVY_HITSTOP = 8;
    public const int COUNTER_HITSTOP = 11;
    public const int BLOCK_HITSTOP = 2;

    // The hit that ends the round holds a lot longer, that is the K.O. freeze
    public const int KO_HITSTOP = 36;

    /* Meter. Landing hits builds it fastest, eating them builds it too, so does blocking and even swinging at nothing.
       The moves that cost meter say so themselves (meter_cost), see Player.Meter */
    public const float METER_PER_DAMAGE_DEALT = 1.0f;
    public const float METER_PER_DAMAGE_TAKEN = 0.6f;
    public const int METER_ON_BLOCK = 3;
    public const int METER_ON_BLOCKED = 2;
    public const int METER_ON_WHIFF = 1;
}
