using System;
using System.Collections.Generic;
using System.Numerics;

using Fighter2D.Content;

using Horizon.HIDL.Runtime;

namespace Fighter2D.Map;

/// <summary>
/// Reads the maps of the game out of Assets/data/maps.hor. Throws (saying which map is wrong) when the file is fucked.
/// </summary>
internal static class MapLoader
{
    // spawn_pos of maps.hor counts its rows down from this one
    public const int SPAWN_BASE_ROW = 32;

    private const float DEFAULT_AMBIENCE_RATE = 14.0f;
    private const float DEFAULT_AMBIENCE_SIZE = 1.5f;
    private const float DEFAULT_AMBIENCE_LIFETIME = 9.0f;

    private static readonly Vector3 DefaultStormColor = new(0.75f, 0.82f, 1.0f);

    // What a map gets when it says nothing about its ambience, a few pink petals on the wind
    private static readonly AmbienceDefinition DefaultAmbience = new()
    {
        StartColor = new Vector3(1.0f, 0.75f, 0.85f),
        EndColor = new Vector3(0.5f, 0.3f, 0.4f),
        Gravity = new Vector2(-12, -6),
        Rate = DEFAULT_AMBIENCE_RATE,
        Size = DEFAULT_AMBIENCE_SIZE,
        Lifetime = DEFAULT_AMBIENCE_LIFETIME
    };

    /// <summary>
    /// The storm of a map that has none of its own, for when the players ask for one anyway.
    /// </summary>
    public static readonly StormDefinition DefaultStorm = new()
    {
        Enabled = true,
        MinInterval = 5.0f,
        MaxInterval = 12.0f,
        Color = DefaultStormColor,
        Brightness = 0.35f,
        Shake = 2.0f
    };

    /// <summary>
    /// Helper method to read every map of the game, from wherever the content is right now.
    /// </summary>
    public static List<MapDefinition> LoadAll() => [.. LoadDefinitions(GameContent.PathOf(GameContent.MAPS_FILE))];

    /// <summary>
    /// Helper method to find a map by the name of its file, its file without the .tmx or its pretty name.
    /// </summary>
    public static bool TryFind(string name, out MapDefinition found)
    {
        foreach (MapDefinition map in LoadAll())
        {
            if (map.FileName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                map.FileName.Equals(name + ".tmx", StringComparison.OrdinalIgnoreCase) ||
                map.PrettyName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                found = map;
                return true;
            }
        }

        found = default;
        return false;
    }

    public static IEnumerable<MapDefinition> LoadDefinitions(string file)
    {
        foreach (var (id, value) in HorReader.LoadObject(file, "maps"))
        {
            if (value is not ObjectValue { Properties: { } map }) throw new Exception($"'{file}': the map '{id}' has to be an object.");

            MapDefinition read;
            try
            {
                read = ReadMap(id, map);
            }
            catch (Exception e)
            {
                throw new Exception($"'{file}': the map '{id}' is wrong: {e.Message}");
            }

            yield return read;
        }
    }

    private static MapDefinition ReadMap(string id, Dictionary<string, IRuntimeValue> map)
    {
        // A map that marks its spawns in the .tmx doesn't need a spawn_pos
        Vector2 spawn = Vector2.Zero;
        if (HorReader.TryObject(map, "spawn_pos", out var spawnPos))
        {
            spawn = new Vector2(HorReader.Number(spawnPos, "x", 0), HorReader.Number(spawnPos, "y", 0));
        }

        // A map that says nothing about its lighting is as bright everywhere as it was painted
        HorReader.TryObject(map, "lighting", out var lighting);

        return new MapDefinition
        {
            FileName = HorReader.Text(map, "file_name"),
            PrettyName = HorReader.Text(map, "pretty_name", id),
            Description = HorReader.Text(map, "description", string.Empty),
            SpawnPosition = spawn,
            Ambience = ReadAmbience(map),
            Lighting = new LightingDefinition { Ambient = HorReader.Colour(lighting, "ambient", Vector3.One) },
            Storm = ReadStorm(map)
        };
    }

    private static AmbienceDefinition ReadAmbience(Dictionary<string, IRuntimeValue> map)
    {
        if (!HorReader.TryObject(map, "ambience", out var ambience)) return DefaultAmbience;

        return new AmbienceDefinition
        {
            StartColor = HorReader.Colour(ambience, "start_colour", DefaultAmbience.StartColor),
            EndColor = HorReader.Colour(ambience, "end_colour", DefaultAmbience.EndColor),

            // One or the other. Gravity for what drifts, velocity for what falls
            Gravity = HorReader.Vector(ambience, "gravity", Vector2.Zero),
            Velocity = ambience.TryGetValue("velocity", out var velocity) ? HorReader.Vector(velocity, "velocity") : null,

            Rate = MathF.Max(0.0f, HorReader.Number(ambience, "rate", DEFAULT_AMBIENCE_RATE)),
            Size = MathF.Max(0.1f, HorReader.Number(ambience, "size", DEFAULT_AMBIENCE_SIZE)),
            Lifetime = MathF.Max(0.1f, HorReader.Number(ambience, "lifetime", DEFAULT_AMBIENCE_LIFETIME)),
            Stretch = MathF.Max(0.0f, HorReader.Number(ambience, "stretch", 0.0f)),
            Splash = MathF.Max(0.0f, HorReader.Number(ambience, "splash", 0.0f)),
            SplashFade = Math.Clamp(HorReader.Number(ambience, "splash_fade", 0.0f), 0.0f, 1.0f)
        };
    }

    /// <summary>
    /// Helper method to read the storm of a map, which most of them don't have.
    /// </summary>
    private static StormDefinition ReadStorm(Dictionary<string, IRuntimeValue> map)
    {
        if (!HorReader.TryObject(map, "storm", out var storm)) return default;

        // The interval is either one number or the shortest and the longest as a vec
        Vector2 interval = new(8.0f, 18.0f);
        if (storm.TryGetValue("interval", out var intervalValue))
        {
            if (intervalValue is Vector2Value range) interval = range.Value;
            else if (intervalValue is NumberValue every) interval = new Vector2(every.Value);
        }

        return new StormDefinition
        {
            Enabled = true,
            MinInterval = MathF.Max(0.5f, MathF.Min(interval.X, interval.Y)),
            MaxInterval = MathF.Max(0.5f, MathF.Max(interval.X, interval.Y)),
            Color = HorReader.Colour(storm, "colour", DefaultStormColor),
            Brightness = HorReader.Number(storm, "brightness", 0.35f),
            Shake = HorReader.Number(storm, "shake", 2.0f)
        };
    }
}
