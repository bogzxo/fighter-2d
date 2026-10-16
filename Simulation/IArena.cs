using System.Collections.Generic;
using System.Numerics;

namespace Fighter2D.Simulation;

/// <summary>
/// One stop of a way across the stage, where the feet go and whether it is jumped to from the stop before.
/// </summary>
internal readonly record struct RouteStep(Vector2 Position, bool Jump);

/// <summary>
/// What the simulation wants of the stage a fight is on, which is a way across it for whoever walks without a thumb
/// steering them (the dummy, the winner going over to gloat). <see cref="FightingStage"/> is one over the tile map's
/// pathfinder, a test has none and everybody walks straight at things.
/// </summary>
internal interface IArena
{
    /// <summary>Whether there is a map to find a way on at all.</summary>
    bool HasPaths { get; }

    /// <summary>How wide a tile is, and where the left edge of the first one is, for standing somewhere in a tile rather than the middle.</summary>
    float TileWidth { get; }
    float OriginX { get; }

    /// <summary>
    /// Finds the way from one spot to another over the floor of the stage, for somebody of a size who jumps so high and so far.
    /// False if there is none, the steps are then whatever they were.
    /// </summary>
    bool TryFindPath(Vector2 from, Vector2 to, Vector2 bodySize, float jumpHeight, float jumpDistance, List<RouteStep> steps);
}
