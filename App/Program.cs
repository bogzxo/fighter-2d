using Horizon.Logging;
using System;

using Horizon.Core;
using Horizon.Engine;

namespace Fighter2D.App;

internal class Program
{
    // Start the game with "--map underground" to skip the menus and drop straight into a fight against the dummy.
    // Add "--character cammy" to play as somebody else than the first character. Handy for working on a map or the fight itself
    private const string ARGUMENT_MAP = "--map";
    private const string ARGUMENT_CHARACTER = "--character";

    // "--content C:/wherever/Fighter2D" loads the moves, characters, maps and art from that folder (the one with Assets in it)
    // instead of from the copy next to the exe. With the host tools of the pause menu that is edit a file, reload, see it, no build in between
    private const string ARGUMENT_CONTENT = "--content";

    private static void Main(string[] args)
    {
        // Unpack native libraries and shaders to the same folder as the executable, so that the game can find them
        PackedFiles.Unpack();

        // Whatever the player set on the options screen last time, the window is made the way they left it
        GameOptions.Load();

        if (TryGetArgument(args, ARGUMENT_CONTENT, out string content))
        {
            if (Directory.Exists(Path.Combine(content, GameContent.MAPS_DIRECTORY))) GameContent.UseHome(content);
            else Log.Warning($"There is no content in '{content}', going with what the game came with.");
        }

        var engine = new GameEngine(WindowManagerConfiguration.Default1600x900 with
        {
            WindowTitle = Constants.WINDOW_TITLE,
            WindowSize = GameOptions.Resolution,
            VSync = GameOptions.VSync,
            FramesPerSecond = GameOptions.FrameLimit
        });

        // Fullscreen is gone into the same way the options screen does it, which is before the first scene is made
        if (GameOptions.Fullscreen) engine.WindowManager.Apply(GameOptions.Display);

        Screen.ApplyTransitions();

        // Every scene reads its gamepads through here, so the bindings have to be in before the first one
        GameInput.Attach(engine);

        // Keeps the connection of an online fight alive from the lobby into the fight
        engine.AddEntity(new NetPump());

        // FPS, the loops, garbage: F3 goes round it, the options screen sets where it starts
        Screen.Performance = engine.AddEntity(new Horizon.UI.PerformanceOverlay(GameOptions.Performance));

        if (TryGetArgument(args, ARGUMENT_MAP, out string mapName) && TryFindMap(mapName, out MapDefinition map))
        {
            TryGetArgument(args, ARGUMENT_CHARACTER, out string character);

            FightScene CreateFight(FightResume? resume = null) => new(MapLoader.TryFind(map.FileName, out MapDefinition fresh) ? fresh : map, 0)
            {
                CharacterId = character.Length > 0 ? character : null,
                Resume = resume,
                Rematch = () => CreateFight(),
                Reload = CreateFight
            };

            engine.Run(CreateFight(), Screen.IntoFight);
        }
        else
        {
            engine.Run<MainMenuScene>();
        }
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

        Log.Warning($"There is no map called '{name}', starting at the menu instead.");
        return false;
    }
}
