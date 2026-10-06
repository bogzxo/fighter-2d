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

    public const string ACTION_X = "punch_left";
    public const string ACTION_Y = "punch_right";

    // No move of ours is on these yet, they are there for the move files to use
    public const string ACTION_LEFT_BUMPER = "left_bumper";
    public const string ACTION_LEFT_TRIGGER = "left_trigger";
    public const string ACTION_RIGHT_TRIGGER = "right_trigger";

    // Two buttons at once on a single one, for whoever would rather not press both. Holding the two buttons themselves does the same
    public const string ACTION_A_B = "combo_a_b";
    public const string ACTION_X_Y = "combo_x_y";
    public const string ACTION_A_X = "combo_a_x";
    public const string ACTION_B_Y = "combo_b_y";

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
        new(ACTION_LEFT_BUMPER, "Left bumper", InputFlags.LeftBumper),

        new(ACTION_X, "Left punch", InputFlags.X),
        new(ACTION_Y, "Right punch", InputFlags.Y),
        new(ACTION_LEFT_TRIGGER, "Left trigger", InputFlags.LeftTrigger),
        new(ACTION_RIGHT_TRIGGER, "Right trigger", InputFlags.RightTrigger),
        new(ACTION_A_B, "A + B at once", InputFlags.A | InputFlags.B),
        new(ACTION_X_Y, "X + Y at once", InputFlags.X | InputFlags.Y),
        new(ACTION_A_X, "A + X at once", InputFlags.A | InputFlags.X),
        new(ACTION_B_Y, "B + Y at once", InputFlags.B | InputFlags.Y),
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
            .Bind(ACTION_BLOCK, GamepadInput.RightBumper)
            .Bind(ACTION_LEFT_BUMPER, GamepadInput.LeftBumper)
            .Bind(ACTION_X, GamepadInput.X)
            .Bind(ACTION_Y, GamepadInput.Y)
            .Bind(ACTION_LEFT_TRIGGER, GamepadInput.LeftTrigger)
            .Bind(ACTION_RIGHT_TRIGGER, GamepadInput.RightTrigger)

            // Not on anything until somebody wants them
            .Bind(ACTION_A_B)
            .Bind(ACTION_X_Y)
            .Bind(ACTION_A_X)
            .Bind(ACTION_B_Y);
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
    public static bool MenuLeftPressed(Gamepad gamepad) => gamepad.WasPressed(GamepadInput.DPadLeft) || gamepad.WasPressed(GamepadInput.LeftStickLeft);
    public static bool MenuRightPressed(Gamepad gamepad) => gamepad.WasPressed(GamepadInput.DPadRight) || gamepad.WasPressed(GamepadInput.LeftStickRight);

    // What a text starts with to be shown with the icons of a PlayStation gamepad. Which icons those are is the skins
    // to say (icon_sets in skin.hor), the texts themselves are all written with the buttons of an Xbox gamepad
    private const string PLAYSTATION_ICONS = "[icons:playstation]";

    // Hints are asked for on every update, so every text is only ever translated once
    private static readonly Dictionary<string, string> playStationTexts = [];

    /// <summary>
    /// Helper method to make text show the icons of the correct gamepad, all texts are written with the icons of an Xbox gamepad
    /// (pad_a, pad_rb) and come back saying they are for a PlayStation gamepad if that is what it is for
    /// </summary>
    /// <param name="gamepad">The gamepad the text is for, null leaves the text as it is.</param>
    public static string Localize(string text, Gamepad? gamepad)
    {
        if (gamepad?.Kind != GamepadKind.PlayStation) return text;

        lock (playStationTexts)
        {
            if (playStationTexts.TryGetValue(text, out string? known)) return known;

            // Nothing on a PlayStation gamepad is called start
            return playStationTexts[text] = PLAYSTATION_ICONS + text.Replace("start cancels", "options cancels");
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
