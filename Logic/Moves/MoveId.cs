namespace Fighter2D.Logic.Moves;

/// <summary>
/// The moves the game itself reaches for, by the name they have to go by in the move files.
/// Every other move is only ever found through an input or a reroute, and can be called whatever its file likes.
/// </summary>
public static class MoveIds
{
    // What a player does when they are doing nothing, every move list has to have it
    public const string IDLE = "idle";

    // What a player is put in for as long as they are stunned
    public const string HIT_STUN = "hit_stun";

    // What a player is put in after a long fall, if the move list has it
    public const string HEAVY_LAND = "heavy_land";

    // For a controller that hasn't picked a move yet
    public const string NONE = "";
}
