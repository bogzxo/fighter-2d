using System;
using System.Collections.Generic;
using System.Text;

using Fighter2D.Logic;
using Horizon.Engine;
using Horizon.Input2;

namespace Fighter2D;

/// <summary>
/// The gamepads of the game and what their buttons are bound to, shared by every scene.
/// A fight never asks for a button, it asks for one of the actions below, which every gamepad can have on different buttons.
/// </summary>
internal static class GameInput
{
    // Where the bindings of every gamepad are kept between runs, next to the game
    public const string BINDINGS_FILE = "gamepads.hor";

    public const string ACTION_LEFT = "move_left";
    public const string ACTION_RIGHT = "move_right";
    public const string ACTION_JUMP = "jump";
    public const string ACTION_CROUCH = "crouch";
    public const string ACTION_KICK_LEFT = "kick_left";
    public const string ACTION_KICK_RIGHT = "kick_right";
    public const string ACTION_BLOCK = "block";

    /// <summary>
    /// An action of the fight, what it is called on screen and the button of the move list it stands for.
    /// </summary>
    public readonly record struct FightAction(string Name, string Label, InputFlags Flag);

    // The move list still thinks in the buttons it was written with, an action is simply one of those wherever it is bound to
    public static readonly FightAction[] Actions =
    [
        new(ACTION_LEFT, "Move left", InputFlags.DPadLeft),
        new(ACTION_RIGHT, "Move right", InputFlags.DPadRight),
        new(ACTION_JUMP, "Jump", InputFlags.DPadUp),
        new(ACTION_CROUCH, "Crouch", InputFlags.DPadDown),
        new(ACTION_KICK_LEFT, "Left kick", InputFlags.A),
        new(ACTION_KICK_RIGHT, "Right kick", InputFlags.B),
        new(ACTION_BLOCK, "Block", InputFlags.RightBumper),
    ];

    public static GamepadInputManager Manager { get; private set; } = null!;

    /// <summary>
    /// Helper method to create the gamepad manager and hand it to the engine, which updates it from then on.
    /// Has to be called once, before the first scene.
    /// </summary>
    public static void Attach(GameEngine engine)
    {
        Manager = new GamepadInputManager();
        Manager.DefaultBindings.CopyFrom(CreateDefaultBindings());

        // No file simply means nobody has changed anything yet
        if (System.IO.File.Exists(BINDINGS_FILE) && Manager.Load(BINDINGS_FILE, out var problems))
        {
            foreach (string problem in problems)
            {
                Console.WriteLine($"[GameInput] {BINDINGS_FILE}: {problem}");
            }

            // The file remembers the defaults it was saved with, ours may have changed since
            Manager.DefaultBindings.CopyFrom(CreateDefaultBindings());
        }

        engine.AddEntity(Manager);
    }

    /// <summary>
    /// Helper method to write the bindings of every gamepad back to the file.
    /// </summary>
    public static void Save()
    {
        try
        {
            Manager.Save(BINDINGS_FILE);
        }
        catch (Exception e)
        {
            // Not being able to save is no reason to stop the game
            Console.WriteLine($"[GameInput] Could not save {BINDINGS_FILE}: {e.Message}");
        }
    }

    /// <summary>
    /// The bindings a gamepad has until somebody changes them.
    /// </summary>
    public static GamepadBindings CreateDefaultBindings()
    {
        return new GamepadBindings()
            .Bind(ACTION_LEFT, GamepadInput.DPadLeft, GamepadInput.LeftStickLeft)
            .Bind(ACTION_RIGHT, GamepadInput.DPadRight, GamepadInput.LeftStickRight)
            .Bind(ACTION_JUMP, GamepadInput.DPadUp, GamepadInput.LeftStickUp)
            .Bind(ACTION_CROUCH, GamepadInput.DPadDown, GamepadInput.LeftStickDown)
            .Bind(ACTION_KICK_LEFT, GamepadInput.A)
            .Bind(ACTION_KICK_RIGHT, GamepadInput.B)
            .Bind(ACTION_BLOCK, GamepadInput.RightBumper);
    }

    /// <summary>
    /// Helper method to turn everything a gamepad holds into the buttons of the move list, through its bindings.
    /// </summary>
    public static InputFlags ReadFight(Gamepad gamepad)
    {
        InputFlags held = InputFlags.None;

        foreach (var action in Actions)
        {
            if (gamepad.IsDown(action.Name)) held |= action.Flag;
        }

        return held;
    }

    // Menus are not rebindable, otherwise a bad binding could lock somebody out of the screen
    public static bool MenuUp(Gamepad gamepad) => gamepad.IsDown(GamepadInput.DPadUp) || gamepad.IsDown(GamepadInput.LeftStickUp);
    public static bool MenuDown(Gamepad gamepad) => gamepad.IsDown(GamepadInput.DPadDown) || gamepad.IsDown(GamepadInput.LeftStickDown);
    public static bool MenuLeft(Gamepad gamepad) => gamepad.IsDown(GamepadInput.DPadLeft) || gamepad.IsDown(GamepadInput.LeftStickLeft);
    public static bool MenuRight(Gamepad gamepad) => gamepad.IsDown(GamepadInput.DPadRight) || gamepad.IsDown(GamepadInput.LeftStickRight);

    // Only true on the update the direction was pushed, for menus that move one entry at a time
    public static bool MenuUpPressed(Gamepad gamepad) => gamepad.WasPressed(GamepadInput.DPadUp) || gamepad.WasPressed(GamepadInput.LeftStickUp);
    public static bool MenuDownPressed(Gamepad gamepad) => gamepad.WasPressed(GamepadInput.DPadDown) || gamepad.WasPressed(GamepadInput.LeftStickDown);

    // The icons of the skin are named after the buttons of an Xbox gamepad, these are the same buttons on a PlayStation one
    private static readonly (string Xbox, string PlayStation)[] PlayStationIcons =
    [
        ("icon:pad_a]", "icon:ps_cross]"),
        ("icon:pad_b]", "icon:ps_circle]"),
        ("icon:pad_x]", "icon:ps_square]"),
        ("icon:pad_y]", "icon:ps_triangle]"),
        ("icon:pad_lb]", "icon:ps_l1]"),
        ("icon:pad_rb]", "icon:ps_r1]"),
        ("icon:pad_lt]", "icon:ps_l2]"),
        ("icon:pad_rt]", "icon:ps_r2]"),
        ("icon:pad_ls]", "icon:ps_l3]"),
        ("icon:pad_rs]", "icon:ps_r3]"),
    ];

    // Hints are asked for on every update, so every text is only ever translated once
    private static readonly Dictionary<string, string> playStationTexts = [];

    /// <summary>
    /// Helper method to make a text show the buttons of the gamepad it is about: texts are written with the icons of an Xbox gamepad
    /// (pad_a, pad_rb) and come back with the ones of a PlayStation gamepad if that is what the player is holding.
    /// </summary>
    /// <param name="gamepad">The gamepad the text is for, null leaves the text as it is.</param>
    public static string Localize(string text, Gamepad? gamepad)
    {
        if (gamepad?.Kind != GamepadKind.PlayStation) return text;

        lock (playStationTexts)
        {
            if (playStationTexts.TryGetValue(text, out string? known)) return known;

            string translated = text;
            foreach (var (xbox, playStation) in PlayStationIcons)
            {
                translated = translated.Replace(xbox, playStation);
            }

            // Nothing on a PlayStation gamepad is called start
            translated = translated.Replace("start cancels", "options cancels");

            return playStationTexts[text] = translated;
        }
    }

    /// <summary>
    /// Helper method to write out what an action is bound to on a gamepad, as a text for a label (with the icons of the skin)
    /// </summary>
    public static string Describe(Gamepad gamepad, string action) => Localize(Describe(gamepad.Bindings, action), gamepad);

    /// <summary>
    /// Helper method to write out buttons that are held together on a gamepad, as a text for a label.
    /// </summary>
    public static string Describe(Gamepad gamepad, uint combination) => Localize(Describe(combination), gamepad);

    /// <summary>
    /// Helper method to write out what an action is bound to, with the icons of an Xbox gamepad (see <see cref="Localize"/>)
    /// </summary>
    public static string Describe(GamepadBindings bindings, string action)
    {
        var text = new StringBuilder();

        foreach (uint combination in bindings.CombinationsOf(action))
        {
            if (text.Length > 0) text.Append("  or  ");
            text.Append(Describe(combination));
        }

        return text.Length > 0 ? text.ToString() : "not bound";
    }

    /// <summary>
    /// Helper method to write out buttons that are held together, as a text for a label.
    /// </summary>
    public static string Describe(uint combination)
    {
        var text = new StringBuilder();

        foreach (GamepadInput input in GamepadInputs.FromMask(combination))
        {
            if (text.Length > 0) text.Append(" + ");
            text.Append(Describe(input));
        }

        return text.ToString();
    }

    /// <summary>
    /// Helper method to get the icon of a gamepad input, using the UI skin for the icons.
    /// </summary>
    public static string Describe(GamepadInput input) => input switch
    {
        GamepadInput.A => "[icon:pad_a]",
        GamepadInput.B => "[icon:pad_b]",
        GamepadInput.X => "[icon:pad_x]",
        GamepadInput.Y => "[icon:pad_y]",
        GamepadInput.LeftBumper => "[icon:pad_lb]",
        GamepadInput.RightBumper => "[icon:pad_rb]",
        GamepadInput.LeftTrigger => "[icon:pad_lt]",
        GamepadInput.RightTrigger => "[icon:pad_rt]",
        GamepadInput.LeftStick => "[icon:pad_ls] click",
        GamepadInput.RightStick => "[icon:pad_rs] click",
        GamepadInput.DPadUp => "[icon:dpad] up",
        GamepadInput.DPadDown => "[icon:dpad] down",
        GamepadInput.DPadLeft => "[icon:dpad] left",
        GamepadInput.DPadRight => "[icon:dpad] right",
        GamepadInput.LeftStickUp => "[icon:pad_ls] up",
        GamepadInput.LeftStickDown => "[icon:pad_ls] down",
        GamepadInput.LeftStickLeft => "[icon:pad_ls] left",
        GamepadInput.LeftStickRight => "[icon:pad_ls] right",
        GamepadInput.RightStickUp => "[icon:pad_rs] up",
        GamepadInput.RightStickDown => "[icon:pad_rs] down",
        GamepadInput.RightStickLeft => "[icon:pad_rs] left",
        GamepadInput.RightStickRight => "[icon:pad_rs] right",
        _ => input.ToString()
    };
}
