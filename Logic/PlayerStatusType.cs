namespace Fighter2D.Logic;

/// <summary>
/// What a player is busy with as far as getting hit goes. The moves set these, apart from hitstun which the hit that caused it sets.
/// </summary>
public enum PlayerStatusType
{
    Normal,
    Attacking,     // in a move that throws a hit
    Blocking,      // hits from the front bounce off
    Invulnerable,  // i-frames, hits go straight through (dodge roll)
    Hitstun        // just got smacked and has no controls until it wears off
}
