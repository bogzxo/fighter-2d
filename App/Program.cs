using Horizon.Content;
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
    private const string ARGUMENT_OPPONENT = "--opponent";
    private const string ARGUMENT_POSITIONS = "--positions";
    private const string ARGUMENT_DUMMY = "--dummy";

    // "--content C:/wherever/Fighter2D" loads the moves, characters, maps and art from that folder (the one with Assets in it)
    // instead of from the copy next to the exe. With the host tools of the pause menu that is edit a file, reload, see it, no build in between
    private const string ARGUMENT_CONTENT = "--content";

    // "--windowed" keeps it out of fullscreen for this once whatever the options say, the hitbox editor starts it like that
    private const string ARGUMENT_WINDOWED = "--windowed";

    private static void Main(string[] args)
    {
        // Unpack native libraries and shaders to the same folder as the executable, so that the game can find them
        PackedFiles.Unpack();

        // Whatever the player set on the options screen last time, the window is made the way they left it
        GameOptions.Load();
        if (args.Contains(ARGUMENT_WINDOWED)) GameOptions.Fullscreen = false;

        // Every text of the game, the window's title among them, comes out of Assets/ui/localisation in the language
        // the options say. One that isn't there falls back on British English, which is always there
        if (!Localisation.Load(Constants.LOCALISATION_DIRECTORY, GameOptions.Language))
            Localisation.Load(Constants.LOCALISATION_DIRECTORY, Localisation.DEFAULT_LANGUAGE);

        // The combat rules of the content the game came with, read again below if it was told to use other content
        CombatRules.Load();

        if (TryGetArgument(args, ARGUMENT_CONTENT, out string content))
        {
            if (Directory.Exists(Path.Combine(content, GameContent.MAPS_DIRECTORY)))
            {
                GameContent.UseHome(content);

                // Worked on where it's kept, the fight reloads what's saved there by itself (a move saved in Hex)
                ContentWatcher.Start(content);
            }
            else Log.Warning($"There is no content in '{content}', going with what the game came with.");
        }

        var engine = new GameEngine(WindowManagerConfiguration.Default1600x900 with
        {
            WindowTitle = Constants.WindowTitle,
            WindowSize = GameOptions.Resolution,
            VSync = GameOptions.VSync,
            FramesPerSecond = GameOptions.FrameLimit
        });

        // Fullscreen is gone into the same way the options screen does it, which is before the first scene is made
        if (GameOptions.Fullscreen) engine.WindowManager.Apply(GameOptions.Display);

        Screen.ApplyTransitions();
        Screen.ApplySkin();

        // Every scene reads its gamepads through here, so the bindings have to be in before the first one
        GameInput.Attach(engine);
        GameSounds.ApplyVolumes();

        // Keeps the connection of an online fight alive from the lobby into the fight
        engine.AddEntity(new NetPump());

        // FPS, the loops, garbage: F3 goes round it, the options screen sets where it starts
        Screen.Performance = engine.AddEntity(new Horizon.UI.PerformanceOverlay(GameOptions.Performance));

        if (TryGetArgument(args, ARGUMENT_MAP, out string mapName) && TryFindMap(mapName, out MapDefinition map))
        {
            TryGetArgument(args, ARGUMENT_CHARACTER, out string character);

            // The dummy's character, which the training menu can change mid fight
            TryGetArgument(args, ARGUMENT_OPPONENT, out string dummy);

            // --positions 520,600 puts player one at 520 and player two at 600 at the start of every round, facing each other,
            // so a scripted test starts from exactly the same place every time
            (float, float)? positions = null;
            if (TryGetArgument(args, ARGUMENT_POSITIONS, out string placed) && placed.Split(',') is [var one, var two]
                && float.TryParse(one, System.Globalization.CultureInfo.InvariantCulture, out float x1)
                && float.TryParse(two, System.Globalization.CultureInfo.InvariantCulture, out float x2))
                positions = (x1, x2);

            // --dummy stand (or crouch, block, crouchblock) has the dummy do that from the first tick, the AI switched off
            DummyMode? dummyMode = TryGetArgument(args, ARGUMENT_DUMMY, out string mode) && Enum.TryParse(mode, ignoreCase: true, out DummyMode parsed) ? parsed : null;

            // A practice fight set up the way the setup screen would have, player one on the first gamepad
            var setup = new MatchSetup(MatchMode.Practice) { TestPositions = positions, StartingDummyMode = dummyMode, DummyCharacter = dummy };
            setup.Slots.Add(0);
            if (character.Length > 0) setup.SetCharacter(0, character);

            // Both got ready on workers while the fight is made, the way the setup screen does it, which halves the wait
            CharacterArt.PrepareAhead(CharacterDefinition.Load(character));
            CharacterArt.PrepareAhead(CharacterDefinition.Load(dummy));

            engine.Run(setup.CreateFight(map), Screen.IntoFight);
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
