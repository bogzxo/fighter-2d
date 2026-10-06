using System;
using System.Linq;

using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Engine;

namespace Fighter2D;

internal class Program
{
    private const string ARGUMENT_MAP = "--map";
    private const string ARGUMENT_CHARACTER = "--character";

    private static void Main(string[] args)
    {
        // Unpack native libraries and shaders to the same folder as the executable, so that the game can find them
        Fighter2D.Content.PackedFiles.Unpack();

        var eng = new GameEngine(new GameEngineConfiguration
        {
            InitialScene = typeof(MainMenuScene),
            WindowConfiguration = WindowManagerConfiguration.Default1600x900 with
            {
                WindowTitle = Constants.WINDOW_TITLE,
                //WindowSize = new System.Numerics.Vector2(1920,1080),
                //Fullscreen = true,
            }
        });

        // Every scene reads its gamepads from here, so it has to be there before the first one
        GameInput.Attach(eng);

        // Keeps the connection of an online fight going from the lobby into the fight
        eng.AddEntity(new Fighter2D.Networking.NetPump());

        if (TryGetMap(args, out var map))
        {
            // As whoever was asked for (--character cammy), or the first character there is
            int character = Array.IndexOf(args, ARGUMENT_CHARACTER);
            eng.SetScene(new FightScene(map, 0) { CharacterId = character >= 0 && character + 1 < args.Length ? args[character + 1] : null });
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
        foreach (var definition in MapLoader.LoadDefinitions(Fighter2D.Content.GameContent.PathOf(Fighter2D.Content.GameContent.MAPS_FILE)))
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
