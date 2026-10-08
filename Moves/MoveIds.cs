namespace Fighter2D.Moves;

/// <summary>
/// The moves the game itself reaches for, by the name they need to have in the move files.
/// Every other move is only found through an input or a reroute and can be called whatever its file likes.
/// </summary>
public static class MoveIds
{
    // What a player does when they are doing fuck all, every move list has to have it
    public const string IDLE = "idle";

    // What a player is put in for as long as they are in hitstun
    public const string HIT_STUN = "hit_stun";

    // What a player is put in once their health is gone, if the move list has it. Without one they stay in hit_stun
    public const string KNOCKED_OUT = "knocked_out";

    // What a player is put in after a long fall, if the move list has it
    public const string HEAVY_LAND = "heavy_land";

    // What a player lies in after a knockdown (if the move list has it, hit_stun otherwise), and what they get back up with
    // once the knockdown is over (if the move list has it, idle otherwise). Nothing hits somebody who is down
    public const string KNOCKED_DOWN = "knocked_down";
    public const string GET_UP = "get_up";

    // What the winner of a round plays over whoever they knocked out, if the move list has it (see VictoryTaunt),
    // and the move they walk over there with
    public const string VICTORY = "victory";
    public const string RUN = "run";

    // For a controller that hasn't picked a move yet
    public const string NONE = "";
}
