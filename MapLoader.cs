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
    internal readonly struct AmbienceDefinition
    {
        public readonly Vector3 StartColor { get; init; }
        public readonly Vector3 EndColor { get; init; }
        public readonly Vector2 Gravity { get; init; }
    }

    internal readonly struct LightingDefinition
    {
        // The light there is everywhere on the map, before any of its lights
        public readonly Vector3 Ambient { get; init; }
    }

    internal readonly struct MapDefinition
    {
        public readonly string PrettyName { get; init; }
        public readonly string FileName { get; init; }
        public readonly string Description { get; init; }
        public readonly Vector2 SpawnPosition { get; init; }
        public readonly AmbienceDefinition Ambience { get; init; }
        public readonly LightingDefinition Lighting { get; init; }
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
                        var gravityObj = (Vector2Value)ambienceObj.Properties["gravity"];

                        ambienceDef = new AmbienceDefinition
                        {
                            // Convert 0-255 RGB scale to 0.0-1.0 float scale for the rendering engine
                            StartColor = startColorObj.Value / 255f,
                            EndColor = endColorObj.Value / 255f,
                            Gravity = gravityObj.Value
                        };
                    }
                    else
                    {
                        // Fallback default ambience if missing
                        ambienceDef = new AmbienceDefinition
                        {
                            StartColor = new Vector3(1.0f, 0.75f, 0.85f),
                            EndColor = new Vector3(0.5f, 0.3f, 0.4f),
                            Gravity = new Vector2(-12, -6)
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