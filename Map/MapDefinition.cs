using System.Numerics;

namespace Fighter2D.Map;

/// <summary>
/// A map as maps.hor describes it. The tiles themselves are in its .tmx file, this is everything around them.
/// </summary>
internal readonly struct MapDefinition
{
    public readonly string PrettyName { get; init; }
    public readonly string FileName { get; init; }
    public readonly string Description { get; init; }

    // Where player one starts (in tiles), for maps that don't mark their spawns themselves
    public readonly Vector2 SpawnPosition { get; init; }

    public readonly AmbienceDefinition Ambience { get; init; }
    public readonly LightingDefinition Lighting { get; init; }
    public readonly StormDefinition Storm { get; init; }
}

/// <summary>
/// The particles that fall or drift through the air of a map (rain, petals, embers), "ambience" in maps.hor.
/// </summary>
internal readonly struct AmbienceDefinition
{
    public readonly Vector3 StartColor { get; init; }
    public readonly Vector3 EndColor { get; init; }

    // What pulls on the particles, for the ones that drift off slowly and pick up speed (petals, embers)
    public readonly Vector2 Gravity { get; init; }

    // The speed the particles go at from the moment they show up, for the ones that fall (rain). Used instead of gravity
    public readonly Vector2? Velocity { get; init; }

    // How many show up every second, how big they are (half of a side) and how long they last (in seconds)
    public readonly float Rate { get; init; }
    public readonly float Size { get; init; }
    public readonly float Lifetime { get; init; }

    // How far they are stretched along the way they move (in seconds of it), on top of what the motion blur does
    public readonly float Stretch { get; init; }

    // For rain. Landing stops a drop dead and sends it off along the ground at any speed up to this
    public readonly float Splash { get; init; }

    // How much of its remaining life a landing can cost a drop (0 to 1), so they don't all vanish at once
    public readonly float SplashFade { get; init; }
}

/// <summary>
/// The thunderstorm over a map, "storm" in maps.hor. Most maps don't have one.
/// </summary>
internal readonly struct StormDefinition
{
    public readonly bool Enabled { get; init; }

    // The shortest and the longest wait between two bolts of lightning, in seconds
    public readonly float MinInterval { get; init; }
    public readonly float MaxInterval { get; init; }

    // The colour a bolt lights the map up with, and how bright it is on top of the map's own light
    public readonly Vector3 Color { get; init; }
    public readonly float Brightness { get; init; }

    // How hard the thunder shakes the screen at most, in pixels of the art
    public readonly float Shake { get; init; }
}

internal readonly struct LightingDefinition
{
    // The light there is everywhere on the map before any of its lights are added
    public readonly Vector3 Ambient { get; init; }
}
