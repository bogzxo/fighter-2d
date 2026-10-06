using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Bogz.Logging.Loggers;

using Horizon.HIDL;
using Horizon.HIDL.Runtime;

namespace Fighter2D;

internal static class MapLoader
{
    // What falls or drifts through the air of a map, "ambience" in maps.hor
    internal readonly struct AmbienceDefinition
    {
        public readonly Vector3 StartColor { get; init; }
        public readonly Vector3 EndColor { get; init; }

        // What pulls on the particles, for the ones that drift off slowly and pick up speed (petals, embers)
        public readonly Vector2 Gravity { get; init; }

        // The speed the particles go at from the moment they show up, for the ones that fall (rain). Instead of gravity
        public readonly Vector2? Velocity { get; init; }

        // How many show up every second, how big they are (half of a side), how long they last (in seconds)
        public readonly float Rate { get; init; }
        public readonly float Size { get; init; }
        public readonly float Lifetime { get; init; }

        // How far they are drawn out along their way (in seconds of it), on top of what the motion blur does
        public readonly float Stretch { get; init; }

        // For what splashes: landing stops them dead and sends them off along the ground, either way and at any speed up to this
        public readonly float Splash { get; init; }

        // How much of the time it has left a landing can cost one of them (0 to 1), so they don't all go at once
        public readonly float SplashFade { get; init; }
    }

    // The storm over a map, "storm" in maps.hor. A map that has none is left in peace
    internal readonly struct StormDefinition
    {
        public readonly bool Enabled { get; init; }

        // The shortest and the longest there is between two bolts, in seconds
        public readonly float MinInterval { get; init; }
        public readonly float MaxInterval { get; init; }

        // What a bolt lights the map up with, and how much of it there is on top of the light of the map
        public readonly Vector3 Color { get; init; }
        public readonly float Brightness { get; init; }

        // How far the thunder throws the view about at most, in pixels of the art
        public readonly float Shake { get; init; }
    }

    private const float DEFAULT_AMBIENCE_RATE = 14.0f;
    private const float DEFAULT_AMBIENCE_SIZE = 1.5f;
    private const float DEFAULT_AMBIENCE_LIFETIME = 9.0f;

    internal readonly struct LightingDefinition
    {
        // The light there is everywhere on the map, before any of its lights
        public readonly Vector3 Ambient { get; init; }
    }

    // spawn_pos of maps.hor counts its rows down from this one, for the maps that don't mark where the players start themselves
    public const int SPAWN_BASE_ROW = 32;

    internal readonly struct MapDefinition
    {
        public readonly string PrettyName { get; init; }
        public readonly string FileName { get; init; }
        public readonly string Description { get; init; }
        public readonly Vector2 SpawnPosition { get; init; }
        public readonly AmbienceDefinition Ambience { get; init; }
        public readonly LightingDefinition Lighting { get; init; }
        public readonly StormDefinition Storm { get; init; }
    }

    /// <summary>
    /// Helper method to read a number an object of a definition may or may not have.
    /// </summary>
    private static float ReadNumber(ObjectValue from, string name, float fallback) =>
        from.Properties.TryGetValue(name, out var value) && value is NumberValue number ? number.Value : fallback;

    /// <summary>
    /// Helper method to read the storm of a map, which most of them don't have.
    /// </summary>
    private static StormDefinition ReadStorm(ObjectValue map)
    {
        if (!map.Properties.TryGetValue("storm", out var value) || value is not ObjectValue storm) return default;

        // Either a time or the shortest and the longest of them
        Vector2 interval = new(8.0f, 18.0f);
        if (storm.Properties.TryGetValue("interval", out var intervalValue))
        {
            if (intervalValue is Vector2Value range) interval = range.Value;
            else if (intervalValue is NumberValue every) interval = new Vector2(every.Value);
        }

        return new StormDefinition
        {
            Enabled = true,
            MinInterval = MathF.Max(0.5f, MathF.Min(interval.X, interval.Y)),
            MaxInterval = MathF.Max(0.5f, MathF.Max(interval.X, interval.Y)),

            // Convert 0-255 RGB scale to 0.0-1.0 float scale for the rendering engine
            Color = storm.Properties.TryGetValue("colour", out var colour) && colour is Vector3Value rgb ? rgb.Value / 255f : new Vector3(0.75f, 0.82f, 1.0f),
            Brightness = ReadNumber(storm, "brightness", 0.35f),
            Shake = ReadNumber(storm, "shake", 2.0f)
        };
    }

    public static IEnumerable<MapDefinition> LoadDefinitions(string mapDefinition)
    {
        if (!File.Exists(mapDefinition)) throw new Exception("Map definition file missing!!");

        HIDLRuntime runtime = new();
        (bool success, string result) = runtime.Evaluate(File.ReadAllText(mapDefinition));

        if (!success) throw new Exception(result);

        if (runtime.UserScope.Lookup("maps") is ObjectValue maps)
        {
            foreach (var mapObj in maps.Properties)
            {
                if (mapObj.Value is ObjectValue map)
                {
                    string fileName = ((StringValue)map.Properties["file_name"]).Value;
                    string prettyName = ((StringValue)map.Properties["pretty_name"]).Value;
                    string description = ((StringValue)map.Properties["description"]).Value;

                    var spawnPos = (ObjectValue)map.Properties["spawn_pos"];
                    float x = ((NumberValue)spawnPos.Properties["x"]).Value;
                    float y = ((NumberValue)spawnPos.Properties["y"]).Value;

                    AmbienceDefinition ambienceDef;
                    if (map.Properties.TryGetValue("ambience", out var ambienceValue) && ambienceValue is ObjectValue ambienceObj)
                    {
                        var startColorObj = (Vector3Value)ambienceObj.Properties["start_colour"];
                        var endColorObj = (Vector3Value)ambienceObj.Properties["end_colour"];

                        // One or the other: gravity for what drifts, velocity for what falls
                        Vector2 gravity = ambienceObj.Properties.TryGetValue("gravity", out var gravityValue) && gravityValue is Vector2Value gravityObj ? gravityObj.Value : Vector2.Zero;
                        Vector2? velocity = ambienceObj.Properties.TryGetValue("velocity", out var velocityValue) && velocityValue is Vector2Value velocityObj ? velocityObj.Value : null;

                        ambienceDef = new AmbienceDefinition
                        {
                            // Convert 0-255 RGB scale to 0.0-1.0 float scale for the rendering engine
                            StartColor = startColorObj.Value / 255f,
                            EndColor = endColorObj.Value / 255f,
                            Gravity = gravity,
                            Velocity = velocity,
                            Rate = MathF.Max(0.0f, ReadNumber(ambienceObj, "rate", DEFAULT_AMBIENCE_RATE)),
                            Size = MathF.Max(0.1f, ReadNumber(ambienceObj, "size", DEFAULT_AMBIENCE_SIZE)),
                            Lifetime = MathF.Max(0.1f, ReadNumber(ambienceObj, "lifetime", DEFAULT_AMBIENCE_LIFETIME)),
                            Stretch = MathF.Max(0.0f, ReadNumber(ambienceObj, "stretch", 0.0f)),
                            Splash = MathF.Max(0.0f, ReadNumber(ambienceObj, "splash", 0.0f)),
                            SplashFade = Math.Clamp(ReadNumber(ambienceObj, "splash_fade", 0.0f), 0.0f, 1.0f)
                        };
                    }
                    else
                    {
                        // Fallback default ambience if missing
                        ambienceDef = new AmbienceDefinition
                        {
                            StartColor = new Vector3(1.0f, 0.75f, 0.85f),
                            EndColor = new Vector3(0.5f, 0.3f, 0.4f),
                            Gravity = new Vector2(-12, -6),
                            Rate = DEFAULT_AMBIENCE_RATE,
                            Size = DEFAULT_AMBIENCE_SIZE,
                            Lifetime = DEFAULT_AMBIENCE_LIFETIME
                        };
                    }

                    // A map that says nothing about its lighting is as bright everywhere as it was painted
                    Vector3 ambient = Vector3.One;
                    if (map.Properties.TryGetValue("lighting", out var lightingValue) && lightingValue is ObjectValue lightingObj &&
                        lightingObj.Properties.TryGetValue("ambient", out var ambientValue) && ambientValue is Vector3Value ambientObj)
                    {
                        // Convert 0-255 RGB scale to 0.0-1.0 float scale for the rendering engine
                        ambient = ambientObj.Value / 255f;
                    }

                    yield return new MapDefinition
                    {
                        FileName = fileName,
                        PrettyName = prettyName,
                        Description = description,
                        SpawnPosition = new Vector2(x, y),
                        Ambience = ambienceDef,
                        Lighting = new LightingDefinition { Ambient = ambient },
                        Storm = ReadStorm(map),
                    };
                }
            }
        }
        else
        {
            ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, "Malformed map object definition!");
        }
    }
}