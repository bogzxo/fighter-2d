namespace Fighter2D.Moves;

/// <summary>
/// Where a hit comes in, which decides what kind of block stops it. A low has to be blocked crouching, an overhead standing,
/// a mid is blocked either way. That is what makes somebody guess.
/// </summary>
public enum HitLevel
{
    Mid,
    Low,
    Overhead
}
