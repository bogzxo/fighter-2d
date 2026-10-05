global using static Horizon.Rendering.Tiling<Fighter2D.Map.MapTileTexID>;

using System;
using System.Linq;

using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Engine;

namespace Fighter2D;

internal class Program
{
    private const string ARGUMENT_MAP = "--map";

    private static void Main(string[] args)
    {
        var eng = new GameEngine(new GameEngineConfiguration
        {
            InitialScene = typeof(MainMenuScene),
            WindowConfiguration = WindowManagerConfiguration.Default1600x900 with
            {
                WindowTitle = Constants.WINDOW_TITLE,
            }
        });

        // Every scene reads its gamepads from here, so it has to be there before the first one
        GameInput.Attach(eng);

        if (TryGetMap(args, out var map))
        {
            eng.SetScene(new FightScene(map, 0));
        }
        else
        {
            eng.SceneManager.ChangeInstance<MainMenuScene>();
        }

        eng.Run();
    }

    /// <summary>
    /// Helper method to find the map a fight was asked to start on straight away (--map underground), past all of the menus.
    /// This is for working on a map or on the fight itself, there is nobody to fight but the dummy and no gamepad is needed.
    /// </summary>
    private static bool TryGetMap(string[] args, out MapLoader.MapDefinition map)
    {
        map = default;

        int index = Array.IndexOf(args, ARGUMENT_MAP);
        if (index < 0 || index + 1 >= args.Length) return false;

        // Either the name of the file or the one it goes by in the definitions
        string name = args[index + 1];
        foreach (var definition in MapLoader.LoadDefinitions("Assets/data/maps.hor"))
        {
            if (definition.FileName.Equals(name + ".tmx", StringComparison.OrdinalIgnoreCase) ||
                definition.FileName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                definition.PrettyName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                map = definition;
                return true;
            }
        }

        Console.WriteLine($"There is no map called '{name}', starting at the menu instead.");
        return false;
    }
}
