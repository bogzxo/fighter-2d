using System;

using Fighter2D.Content;
using Fighter2D.Map;
using Fighter2D.Networking;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Engine;

namespace Fighter2D;

internal class Program
{
    // Start the game with "--map underground" to skip the menus and drop straight into a fight against the dummy.
    // Add "--character cammy" to play as somebody else than the first character. Handy for working on a map or the fight itself
    private const string ARGUMENT_MAP = "--map";
    private const string ARGUMENT_CHARACTER = "--character";

    private static void Main(string[] args)
    {
        // Unpack native libraries and shaders to the same folder as the executable, so that the game can find them
        PackedFiles.Unpack();

        var engine = new GameEngine(new GameEngineConfiguration
        {
            InitialScene = typeof(MainMenuScene),
            WindowConfiguration = WindowManagerConfiguration.Default1600x900 with
            {
                WindowTitle = Constants.WINDOW_TITLE,
            }
        });

        // Every scene reads its gamepads from here, so it has to be there before the first one
        GameInput.Attach(engine);

        // Keeps the connection of an online fight alive from the lobby into the fight
        engine.AddEntity(new NetPump());

        if (TryGetArgument(args, ARGUMENT_MAP, out string mapName) && TryFindMap(mapName, out MapDefinition map))
        {
            TryGetArgument(args, ARGUMENT_CHARACTER, out string character);
            engine.SetScene(new FightScene(map, 0) { CharacterId = character.Length > 0 ? character : null });
        }
        else
        {
            engine.SceneManager.ChangeInstance<MainMenuScene>();
        }

        engine.Run();
    }

    /// <summary>
    /// Helper method to get the value that follows an argument on the command line, false if the argument isn't there.
    /// </summary>
    private static bool TryGetArgument(string[] args, string name, out string value)
    {
        int index = Array.IndexOf(args, name);
        value = index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;

        return value.Length > 0;
    }

    private static bool TryFindMap(string name, out MapDefinition map)
    {
        if (MapLoader.TryFind(name, out map)) return true;

        Console.WriteLine($"There is no map called '{name}', starting at the menu instead.");
        return false;
    }
}
