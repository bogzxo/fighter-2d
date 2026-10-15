using System;

namespace Fighter2D.Combat;

/// <summary>
/// Every number that decides what a hit is worth that a move doesn't say for itself. All times are frames of the fight,
/// 60 a second (see FightTicks.TICK_RATE), and distances world units. This is the file to mess with when the game feels off.
/// A move that says its hitstun gets exactly that, nothing in here scales it behind its back. Hex's move editor has a
/// copy of these in its rules.hor, change one and change the other.
/// </summary>
internal static class CombatRules
{
    /* The guess at the hitstun of a move that doesn't say its own (see MoveFrameData). Write the number in instead,
       Hex shows what this comes to. A move starts out BASE_DISADVANTAGE frames minus on hit and earns STARTUP_REWARD frames
       back for every frame of startup, so jabs are a bit minus and slow moves are plus enough to link off */
    public const float BASE_DISADVANTAGE = 4.0f;
    public const float STARTUP_REWARD = 0.75f;
    public const float MAX_ADVANTAGE = 24.0f;
    public const float HITSTUN_PER_DAMAGE = 0.6f;
    public const int GUESSED_MIN_HITSTUN = 12;

    /* Blocking. A blocked hit still costs something, the blocker is stuck in their block for a share of the hitstun
       (unless the move says its blockstun), gets shoved back, and takes a sliver of the damage as chip. Chip never finishes anybody off */
    public const float BLOCKSTUN_SCALE = 0.7f;
    public const int GUESSED_MIN_BLOCKSTUN = 7;
    public const float CHIP_DAMAGE_SCALE = 0.12f;

    // How far a block slides the blocker back along the ground
    public const float BLOCK_PUSH = 23.0f;

    // How far a hit that doesn't launch slides whoever it lands on along the ground, for a move that doesn't say (push).
    // The attacker slides back half of that, so a flurry of jabs doesn't end up inside of them
    public const float PUSH = 16.0f;
    public const float ATTACKER_RECOIL = 0.5f;

    // A hit in the air that doesn't launch still pops whoever it lands on up this high, so aerials can be strung together
    public const float AIR_HIT_POP = 14.0f;

    /* Combos. Every hit after the first does less, so a long combo isn't a round by itself */
    public const float COMBO_DAMAGE_SCALE = 0.88f;
    public const float MIN_COMBO_DAMAGE_SCALE = 0.35f;

    // A counter hit catches somebody in the startup of their own attack, which is on them. A few frames more hitstun on top
    // of what the move says, never a share of it, so the move's number is still the number
    public const int COUNTER_HITSTUN_BONUS = 4;
    public const float COUNTER_DAMAGE_SCALE = 1.25f;

    /* Juggles. Whoever is launched falls with a share of their gravity (their character's hitstun_gravity, or the move's)
       so they can be hit again on the way down. Every hit that lands on them while they are already up there makes them
       that much heavier, so a juggle comes down by itself rather than going on forever. Guilty Gear does it this way,
       the hitstun the move says is left alone */
    public const float JUGGLE_GRAVITY_STEP = 0.1f;

    /* Knockdowns. How long somebody lies on the floor before they get up, during which nothing can hit them.
       Getting up has i-frames of its own, those are the get_up move's business */
    public const int KNOCKDOWN_TICKS = 55;

    /* Hitlag, how long both of them freeze when a hit lands, for a move that doesn't say (hitstop). Smash Ultimate's,
       damage * 0.65 + 6 frames and never more than 30, so a jab is a blink and a smash is a moment. A block freezes
       them for 0.67 of that, a counter hit for 4 frames more. The victim is launched when it ends, not when the hit lands */
    public const float HITLAG_PER_DAMAGE = 0.65f;
    public const int HITLAG_BASE = 6;
    public const int MAX_HITLAG = 30;
    public const float BLOCK_HITLAG_SCALE = 0.67f;
    public const int COUNTER_HITLAG_BONUS = 4;

    // The hit that ends the round holds a lot longer, that is the K.O. freeze
    public const int KO_HITSTOP = 36;

    /* DI. Whoever gets launched turns the launch by holding a direction as the hitlag ends, up to 0.17 radians (9.74°,
       Smash 4 and Ultimate) one way or the other, most from a direction square on to it */
    public const float DI_MAX_ANGLE = 0.17f;

    /* SDI. Every direction pressed during the hitlag shoves the victim that far that way, one press every few frames at
       most. Smash's is 2 units, a fighter here is about twice the size in units */
    public const float SDI_DISTANCE = 4.0f;
    public const int SDI_INTERVAL = 4;

    /* Teching. Block pressed in the last 11 frames before somebody knocked down lands has them get up on the spot (or roll,
       holding a direction) instead of lying there. A press that wasn't in time locks teching out for 40 frames, so it
       can't be mashed */
    public const int TECH_WINDOW = 11;
    public const int TECH_LOCKOUT = 40;

    /* Stale moves. The last 9 moves of a player that landed are remembered, the newest first, and every copy of this one
       in there takes STALE_STEPS[place] of its damage off. Smash's queue */
    public static readonly float[] STALE_STEPS = [0.09f, 0.08f, 0.07f, 0.06f, 0.05f, 0.04f, 0.03f, 0.02f, 0.01f];

    /* Parry. A block that went up this few frames before the hit (or fewer) is a parry, no chip, no blockstun, no shove,
       and the attacker is the one stuck for PARRY_STUN frames. Rivals and Street Fighter's perfect parry */
    public const int PARRY_TICKS = 5;
    public const int PARRY_STUN = 20;

    /* Meter. Landing hits builds it fastest, eating them builds it too, so does blocking and even swinging at nothing.
       The moves that cost meter say so themselves (meter_cost), see Player.Meter */
    public const float METER_PER_DAMAGE_DEALT = 1.0f;
    public const float METER_PER_DAMAGE_TAKEN = 0.6f;
    public const int METER_ON_BLOCK = 3;
    public const int METER_ON_BLOCKED = 2;
    public const int METER_ON_WHIFF = 1;

    /// <summary>
    /// How long a move freezes both of them for when it lands, what it says or Smash's hitlag for the damage it did.
    /// </summary>
    public static int HitstopOf(Moves.FightingMove move, int damage) =>
        move.Hitstop >= 0 ? move.Hitstop : Math.Min(MAX_HITLAG, (int)(damage * HITLAG_PER_DAMAGE + HITLAG_BASE));

    /// <summary>
    /// How long a blocked move freezes both of them for.
    /// </summary>
    public static int BlockHitstopOf(Moves.FightingMove move) => (int)(HitstopOf(move, move.Damage) * BLOCK_HITLAG_SCALE);
}
