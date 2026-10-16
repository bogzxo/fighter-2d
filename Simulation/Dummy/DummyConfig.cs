namespace Fighter2D.Fighters.Inputs.Dummy;

/// <summary>
/// Every number that decides how good the dummy is at the game. Times are in seconds, distances in world units and chances from 0 to 1.
/// Want an easier or a nastier dummy? This is the only file you need.
/// </summary>
internal static class DummyConfig
{
    // The dummy thinks once per tick of the fight, this is how long one of those is
    public const float TICK = FightTicks.TICK_TIME;

    /* Getting around */

    public const float WALK_SPEED_SCALE = 0.45f;   // How fast it walks compared to a player
    public const float ATTACK_RANGE = 56f;         // How close it walks up, kicks reach slightly further
    public const float WALK_PAUSE = 0.35f;         // Wait before walking again, any sooner and it reads as a double tap (roll)

    public const float STUCK_TIME = 0.2f;          // How long it walks without getting anywhere before it tries jumping
    public const float STUCK_DISTANCE = 4f;        // How little it has to have moved in that time to count as stuck
    public const float JUMP_HOLD = 0.08f;          // How long up is held for a jump
    public const float JUMP_COOLDOWN = 0.5f;       // Wait between two jumps, long enough to have landed from the first
    public const float CLIMB_HEIGHT = 40f;         // How far above it the other player has to be for it to jump after them
    public const float CLIMB_RANGE = 140f;         // And how close to them it has to be for that

    public const float ROUTE_TIME = 0.1f;          // How often it works out the way to the other player again, on the tiles of the stage (see StageRoute)

    public const float ROLL_IN_RANGE = 260f;       // How far away the other player has to be for it to roll towards them
    public const float ROLL_IN_CHANCE = 0.4f;      // How often it actually bothers when it thinks about it
    public const float ROLL_THINK_TIME = 1.0f;     // How often it thinks about it while they are that far away
    public const float ROLL_COOLDOWN = 2.5f;       // Wait between two rolls, nobody likes a dummy that rolls around like a hamster

    /* Pressing buttons */

    public const float TAP_DURATION = 0.05f;       // How long a button is held down for a single press
    public const float TAP_GAP = 0.04f;            // How long a button is let go of before it is pressed again, or it wouldn't count as a new press

    /* Attacking */

    public const float REACTION_TIME = 0.3f;       // How long the other player has to be in range before it attacks
    public const float MIN_ATTACK_DELAY = 0.6f;    // Shortest pause between two attacks nobody gave it an opening for
    public const float MAX_ATTACK_DELAY = 1.5f;    // Longest pause between two of them
    public const float PUNCH_CHANCE = 0.35f;       // How often such an attack is a punch rather than a kick
    public const float PUNISH_CHANCE = 0.7f;       // How often it punishes somebody who just whiffed or hit its block
    public const float FOLLOW_UP_CHANCE = 0.75f;   // How often it keeps hitting somebody it put in hitstun
    public const int MAX_FOLLOW_UPS = 3;           // How many hits it lands on somebody in one hitstun before it lets them be
    public const float AIR_ATTACK_CHANCE = 0.6f;   // How often it kicks on the way past somebody it is in the air next to
    public const float AIR_ATTACK_REACH = 1.3f;    // How much further than on the ground it will swing from in the air

    /* Defending */

    public const float BLOCK_CHANCE = 0.55f;       // How often it sees an attack coming and blocks
    public const float BLOCK_LINGER = 0.12f;       // How long the block stays up after there is nothing left to block
    public const float MAX_BLOCK_TIME = 1.2f;      // How long it keeps a block up at the most, whatever the other player is up to
    public const float BLOCK_RANGE = ATTACK_RANGE * 2;   // How close an attack has to start for it to care
    public const float DODGE_JUMP_CHANCE = 0.5f;   // How often it jumps over a dodge roll coming its way
    public const float ROLL_DANGER_RANGE = 220f;   // How close a dodge roll has to start for it to bother jumping
}
