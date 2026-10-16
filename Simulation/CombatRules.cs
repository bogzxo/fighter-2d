using System;

namespace Fighter2D.Simulation;

/// <summary>
/// Every number that decides what a hit is worth that a move doesn't say for itself, and how a fighter gets about when its
/// character doesn't say (<see cref="Movement"/>). The numbers are Assets/data/combat_rules.hor's, that is the file to mess
/// with when the game feels off, and Hex's move editor reads the same one. Here they are only read (<see cref="Load"/>), at
/// start, when the content changes (online the host's comes with the rest) and when the data is reloaded. All times are
/// frames of the fight, 60 a second (see FightTicks.TICK_RATE), and distances world units. The comments by every number
/// say what it does, the file says the same.
/// </summary>
internal static class CombatRules
{
    // How much health and how much meter a fighter has, out of which everything else is counted
    public const byte MAX_HEALTH = 100;
    public const byte MAX_METER = 100;

    /* The guess at the hitstun of a move that doesn't say its own (see MoveFrameData). Write the number in instead,
       Hex shows what this comes to. A move starts out BASE_DISADVANTAGE frames minus on hit and earns STARTUP_REWARD frames
       back for every frame of startup, so jabs are a bit minus and slow moves are plus enough to link off */
    public static float BASE_DISADVANTAGE { get; private set; }
    public static float STARTUP_REWARD { get; private set; }
    public static float MAX_ADVANTAGE { get; private set; }
    public static float HITSTUN_PER_DAMAGE { get; private set; }
    public static int GUESSED_MIN_HITSTUN { get; private set; }

    /* Blocking. A blocked hit still costs something, the blocker is stuck in their block for a share of the hitstun
       (unless the move says its blockstun), gets shoved back, and takes a sliver of the damage as chip. Chip never finishes anybody off */
    public static float BLOCKSTUN_SCALE { get; private set; }
    public static int GUESSED_MIN_BLOCKSTUN { get; private set; }
    public static float CHIP_DAMAGE_SCALE { get; private set; }

    // How far a block slides the blocker back along the ground
    public static float BLOCK_PUSH { get; private set; }

    // How far a hit that doesn't launch slides whoever it lands on along the ground, for a move that doesn't say (push).
    // The attacker slides back half of that, so a flurry of jabs doesn't end up inside of them
    public static float PUSH { get; private set; }
    public static float ATTACKER_RECOIL { get; private set; }

    // A hit in the air that doesn't launch still pops whoever it lands on up this high, so aerials can be strung together
    public static float AIR_HIT_POP { get; private set; }

    /* Combos. Every hit after the first does less, so a long combo isn't a round by itself */
    public static float COMBO_DAMAGE_SCALE { get; private set; }
    public static float MIN_COMBO_DAMAGE_SCALE { get; private set; }

    // A counter hit catches somebody in the startup of their own attack, which is on them. A few frames more hitstun on top
    // of what the move says, never a share of it, so the move's number is still the number
    public static int COUNTER_HITSTUN_BONUS { get; private set; }
    public static float COUNTER_DAMAGE_SCALE { get; private set; }

    /* Landing something buys room. A move whose hit landed (or was blocked) can be left for a move that doesn't attack, a
       jump after whoever was just launched, a run in, a crouch, the block, this many frames after its last active frame,
       whatever its cancel frames say and however long its recovery is. A whiff gets none of it and plays out, so swinging
       at nothing still costs. This is what makes a juggle possible, the attacker is moving again while the victim is still
       in hitstun. The roll isn't movement for this, i-frames aren't free. What the move itself says it cancels into
       (hit_cancel) opens the frame after the hit lands, as before */
    public static int HIT_MOVEMENT_CANCEL { get; private set; }

    /* Juggles. Whoever is launched falls with a share of their gravity (their character's hitstun_gravity, or the move's)
       so they can be hit again on the way down. Every hit that lands on them while they are already up there makes them
       that much heavier, so a juggle comes down by itself rather than going on forever. Guilty Gear does it this way,
       the hitstun the move says is left alone */
    public static float JUGGLE_GRAVITY_STEP { get; private set; }

    /* Knockdowns. How long somebody lies on the floor before they get up, during which nothing can hit them.
       Getting up has i-frames of its own, those are the get_up move's business */
    public static int KNOCKDOWN_TICKS { get; private set; }

    /* Hitlag, how long both of them freeze when a hit lands, for a move that doesn't say (hitstop). Smash Ultimate's,
       damage * 0.65 + 6 frames and never more than 30, so a jab is a blink and a smash is a moment. A block freezes
       them for 0.67 of that, a counter hit for 4 frames more. The victim is launched when it ends, not when the hit lands */
    public static float HITLAG_PER_DAMAGE { get; private set; }
    public static int HITLAG_BASE { get; private set; }
    public static int MAX_HITLAG { get; private set; }
    public static float BLOCK_HITLAG_SCALE { get; private set; }
    public static int COUNTER_HITLAG_BONUS { get; private set; }

    // The hit that ends the round holds a lot longer, that is the K.O. freeze
    public static int KO_HITSTOP { get; private set; }

    /* DI. Whoever gets launched turns the launch by holding a direction as the hitlag ends, up to 0.17 radians (9.74°,
       Smash 4 and Ultimate) one way or the other, most from a direction square on to it */
    public static float DI_MAX_ANGLE { get; private set; }

    /* SDI. Every direction pressed during the hitlag shoves the victim that far that way, one press every few frames at
       most. Smash's is 2 units, a fighter here is about twice the size in units */
    public static float SDI_DISTANCE { get; private set; }
    public static int SDI_INTERVAL { get; private set; }

    /* Teching. Block pressed in the last 11 frames before somebody knocked down lands has them get up on the spot (or roll,
       holding a direction) instead of lying there. A press that wasn't in time locks teching out for 40 frames, so it
       can't be mashed */
    public static int TECH_WINDOW { get; private set; }
    public static int TECH_LOCKOUT { get; private set; }

    /* The input windows. How long a press stays good for, so a button pressed a little early still comes out, the
       longest gap between the two presses of a double tap, and how long after a press started a move the second button
       of a two button move still turns it into that one (nobody presses A and B on the same sixtieth of a second). A
       fighter takes them as it's made, a reload is seen by the next fight */
    public static int INPUT_BUFFER_TICKS { get; private set; }
    public static int DOUBLE_TAP_TICKS { get; private set; }
    public static int CHORD_TICKS { get; private set; }

    /* Stale moves. The last 9 moves of a player that landed are remembered, the newest first, and every copy of this one
       in there takes STALE_STEPS[place] of its damage off. Smash's queue */
    public static float[] STALE_STEPS { get; private set; } = [];

    /* Parry. A block that went up this few frames before the hit (or fewer) is a parry, no chip, no blockstun, no shove,
       and the attacker is the one stuck for PARRY_STUN frames. Rivals and Street Fighter's perfect parry */
    public static int PARRY_TICKS { get; private set; }
    public static int PARRY_STUN { get; private set; }

    /* Meter. Landing hits builds it fastest, eating them builds it too, so does blocking and even swinging at nothing.
       The moves that cost meter say so themselves (meter_cost), see Player.Meter */
    public static float METER_PER_DAMAGE_DEALT { get; private set; }
    public static float METER_PER_DAMAGE_TAKEN { get; private set; }
    public static int METER_ON_BLOCK { get; private set; }
    public static int METER_ON_BLOCKED { get; private set; }
    public static int METER_ON_WHIFF { get; private set; }


    /// <summary>
    /// How a fighter gets about when the movement of its character leaves something out, the rules file's movement block.
    /// </summary>
    public static CharacterMovement Movement { get; private set; } = null!;

    /// <summary>Where the rules are, inside the content.</summary>
    public const string FILE = "Assets/data/combat_rules.hor";

    /// <summary>
    /// Reads the rules out of the content as it is right now. Every key has to be there, a file that leaves one out or
    /// says something that isn't a number throws saying which, the same way a broken move file does.
    /// </summary>
    public static void Load()
    {
        string path = GameContent.PathOf(FILE);
        var rules = HorReader.LoadObject(path, "rules");

        float Number(string key)
        {
            if (!rules.ContainsKey(key)) throw new Exception($"'{path}' has no '{key}', the game needs every one of the rules.");
            return HorReader.Number(rules, key, 0.0f);
        }

        BASE_DISADVANTAGE = Number("base_disadvantage");
        STARTUP_REWARD = Number("startup_reward");
        MAX_ADVANTAGE = Number("max_advantage");
        HITSTUN_PER_DAMAGE = Number("hitstun_per_damage");
        GUESSED_MIN_HITSTUN = (int)MathF.Round(Number("guessed_min_hitstun"));
        BLOCKSTUN_SCALE = Number("blockstun_scale");
        GUESSED_MIN_BLOCKSTUN = (int)MathF.Round(Number("guessed_min_blockstun"));
        CHIP_DAMAGE_SCALE = Number("chip_damage_scale");
        BLOCK_PUSH = Number("block_push");
        PUSH = Number("push");
        ATTACKER_RECOIL = Number("attacker_recoil");
        AIR_HIT_POP = Number("air_hit_pop");
        COMBO_DAMAGE_SCALE = Number("combo_damage_scale");
        MIN_COMBO_DAMAGE_SCALE = Number("min_combo_damage_scale");
        COUNTER_HITSTUN_BONUS = (int)MathF.Round(Number("counter_hitstun_bonus"));
        COUNTER_DAMAGE_SCALE = Number("counter_damage_scale");
        HIT_MOVEMENT_CANCEL = (int)MathF.Round(Number("hit_movement_cancel"));
        JUGGLE_GRAVITY_STEP = Number("juggle_gravity_step");
        KNOCKDOWN_TICKS = (int)MathF.Round(Number("knockdown_ticks"));
        HITLAG_PER_DAMAGE = Number("hitlag_per_damage");
        HITLAG_BASE = (int)MathF.Round(Number("hitlag_base"));
        MAX_HITLAG = (int)MathF.Round(Number("max_hitlag"));
        BLOCK_HITLAG_SCALE = Number("block_hitlag_scale");
        COUNTER_HITLAG_BONUS = (int)MathF.Round(Number("counter_hitlag_bonus"));
        KO_HITSTOP = (int)MathF.Round(Number("ko_hitstop"));
        DI_MAX_ANGLE = Number("di_max_angle");
        SDI_DISTANCE = Number("sdi_distance");
        SDI_INTERVAL = (int)MathF.Round(Number("sdi_interval"));
        TECH_WINDOW = (int)MathF.Round(Number("tech_window"));
        TECH_LOCKOUT = (int)MathF.Round(Number("tech_lockout"));
        INPUT_BUFFER_TICKS = Math.Max(1, (int)MathF.Round(Number("input_buffer")));
        DOUBLE_TAP_TICKS = Math.Max(1, (int)MathF.Round(Number("double_tap")));
        CHORD_TICKS = Math.Max(0, (int)MathF.Round(Number("chord_window")));
        PARRY_TICKS = (int)MathF.Round(Number("parry_ticks"));
        PARRY_STUN = (int)MathF.Round(Number("parry_stun"));
        METER_PER_DAMAGE_DEALT = Number("meter_per_damage_dealt");
        METER_PER_DAMAGE_TAKEN = Number("meter_per_damage_taken");
        METER_ON_BLOCK = (int)MathF.Round(Number("meter_on_block"));
        METER_ON_BLOCKED = (int)MathF.Round(Number("meter_on_blocked"));
        METER_ON_WHIFF = (int)MathF.Round(Number("meter_on_whiff"));

        if (!rules.TryGetValue("stale_steps", out var steps) || steps is not Horizon.HIDL.Runtime.ListValue { Items: { } items })
            throw new Exception($"'{path}' has no stale_steps, a list of how much every copy of a move takes off.");

        var read = new float[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            read[i] = items[i] is Horizon.HIDL.Runtime.NumberValue number ? number.Value : throw new Exception($"'{path}': stale_steps has something in it that isn't a number.");
        }
        STALE_STEPS = read;

        if (!HorReader.TryObject(rules, "movement", out var movement)) throw new Exception($"'{path}' has no movement, what a fighter does when its character doesn't say.");
        try
        {
            Movement = CharacterMovement.FromBlock(movement, fallback: null);
        }
        catch (Exception e)
        {
            throw new Exception($"'{path}': {e.Message}");
        }

        Horizon.Logging.Log.Info($"[Rules] Read the combat rules out of '{path}'.");
    }

    /// <summary>
    /// The same as <see cref="Load"/> for when the content changes under a game that is already going (online, the data
    /// read again). A broken file is logged and the rules stay what they were, a fight isn't the place to fall over.
    /// </summary>
    public static void TryLoad()
    {
        try
        {
            Load();
        }
        catch (Exception e)
        {
            Horizon.Logging.Log.Error($"[Rules] The combat rules weren't read again, they stay as they were: {e.Message}");
        }
    }

    /// <summary>
    /// How long a move freezes both of them for when it lands, what it says or Smash's hitlag for the damage it did.
    /// </summary>
    public static int HitstopOf(FightingMove move, int damage) =>
        move.Hitstop >= 0 ? move.Hitstop : Math.Min(MAX_HITLAG, (int)(damage * HITLAG_PER_DAMAGE + HITLAG_BASE));

    /// <summary>
    /// How long a blocked move freezes both of them for.
    /// </summary>
    public static int BlockHitstopOf(FightingMove move) => (int)(HitstopOf(move, move.Damage) * BLOCK_HITLAG_SCALE);
}
