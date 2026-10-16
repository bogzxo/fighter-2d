using System.Numerics;

namespace Fighter2D.Fighters;

/// <summary>
/// An axis aligned box, used for hitboxes and hurtboxes.
/// </summary>
internal readonly record struct Box(Vector2 Min, Vector2 Max)
{
    public Vector2 Center => (Min + Max) * 0.5f;

    /// <summary>
    /// Tests if two boxes overlap, and hands back the middle of the bit they share.
    /// </summary>
    public bool Overlaps(in Box other, out Vector2 middle)
    {
        Vector2 min = Vector2.Max(Min, other.Min), max = Vector2.Min(Max, other.Max);
        middle = (min + max) * 0.5f;

        return min.X < max.X && min.Y < max.Y;
    }
}
