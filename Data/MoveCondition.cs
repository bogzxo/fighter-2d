namespace Fighter2D.Data;

/// <summary>
/// What a phase of a move waits on, or keeps looping for.
/// </summary>
public enum MoveCondition
{
    None,
    Held,      // one of the inputs of the move is held down
    Hitstun,   // the player is in hitstun
    Jumping,   // the player is on the way up
    Falling,   // the player is on the way down
    Landing,   // the player is stuck in the landing lag of an aerial they came down in the middle of
    Always
}
