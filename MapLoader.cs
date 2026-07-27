using System.Numerics;

using Bogz.Logging.Loggers;

using Horizon.HIDL;
using Horizon.HIDL.Runtime;

namespace CumInstinctDuel;

internal static class MapLoader
{
    internal readonly struct MapDefinition
    {
        public readonly string PrettyName { get; init; }
        public readonly string FileName { get; init; }
        public readonly string Description { get; init; }
        public readonly Vector2 SpawnPosition { get; init; }
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

                    yield return new MapDefinition
                    {
                        FileName = fileName,
                        PrettyName = prettyName,
                        Description = description,
                        SpawnPosition = new Vector2(x, y),
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
