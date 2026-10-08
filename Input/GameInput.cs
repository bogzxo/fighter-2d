using Horizon.Logging;
using System;
using System.IO;

using Horizon.Engine;
using Horizon.Input;

namespace Fighter2D.Input;

/// <summary>
/// The gamepads of the game and what their buttons are bound to, shared by every scene.
/// A fight never asks for a physical button, it asks for one of the actions below, which every gamepad can have on different buttons.
/// The menus have their own fixed buttons (see <see cref="MenuInput"/>) and the button icons are in <see cref="ButtonGlyphs"/>.
/// </summary>
internal static class GameInput
{
    // Where the bindings of every gamepad are kept between runs, next to the game
    public const string BINDINGS_FILE = "gamepads.hor";

    /// <summary>
    /// An action of the fight, what it is called on the bindings screen and the button of the move files it stands for.
    /// </summary>
    public readonly record struct FightAction(string Name, string Label, InputFlags Flag);

    // The move files are written with buttons (A, B, DPadLeft...), an action is simply one of those wherever the player has bound it.
    // The combos are two buttons at once on a single button for whoever would rather not press both, they start out not bound to anything
    public static readonly FightAction[] Actions =
    [
        new("move_left", "Move left", InputFlags.DPadLeft),
        new("move_right", "Move right", InputFlags.DPadRight),
        new("jump", "Jump", InputFlags.DPadUp),
        new("crouch", "Crouch", InputFlags.DPadDown),
        new("kick_left", "Left kick", InputFlags.A),
        new("kick_right", "Right kick", InputFlags.B),
        new("block", "Block", InputFlags.RightBumper),
        new("left_bumper", "Left bumper", InputFlags.LeftBumper),

        new("punch_left", "Left punch", InputFlags.X),
        new("punch_right", "Right punch", InputFlags.Y),
        new("left_trigger", "Left trigger", InputFlags.LeftTrigger),
        new("right_trigger", "Right trigger", InputFlags.RightTrigger),
        new("combo_a_b", "A + B at once", InputFlags.A | InputFlags.B),
        new("combo_x_y", "X + Y at once", InputFlags.X | InputFlags.Y),
        new("combo_a_x", "A + X at once", InputFlags.A | InputFlags.X),
        new("combo_b_y", "B + Y at once", InputFlags.B | InputFlags.Y),
    ];

    public static GamepadInputManager Manager { get; private set; } = null!;

    /// <summary>
    /// Helper method to give the gamepads of the engine the bindings of the game.
    /// Has to be called once, before the first scene.
    /// </summary>
    public static void Attach(GameEngine engine)
    {
        Manager = engine.Input.Gamepads;
        Manager.DefaultBindings.CopyFrom(CreateDefaultBindings());

        // No file simply means nobody has changed anything yet
        if (File.Exists(BINDINGS_FILE) && Manager.Load(BINDINGS_FILE, out var problems))
        {
            foreach (string problem in problems)
            {
                Log.Info($"[GameInput] {BINDINGS_FILE}: {problem}");
            }

            // The file remembers the defaults it was saved with, ours may have changed since
            Manager.DefaultBindings.CopyFrom(CreateDefaultBindings());
        }
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
            Log.Warning($"[GameInput] Could not save {BINDINGS_FILE}: {e.Message}");
        }
    }

    /// <summary>
    /// The bindings a gamepad has until somebody changes them.
    /// </summary>
    public static GamepadBindings CreateDefaultBindings()
    {
        return new GamepadBindings()
            .Bind("move_left", GamepadInput.DPadLeft, GamepadInput.LeftStickLeft)
            .Bind("move_right", GamepadInput.DPadRight, GamepadInput.LeftStickRight)
            .Bind("jump", GamepadInput.DPadUp, GamepadInput.LeftStickUp)
            .Bind("crouch", GamepadInput.DPadDown, GamepadInput.LeftStickDown)
            .Bind("kick_left", GamepadInput.A)
            .Bind("kick_right", GamepadInput.B)
            .Bind("block", GamepadInput.RightBumper)
            .Bind("left_bumper", GamepadInput.LeftBumper)
            .Bind("punch_left", GamepadInput.X)
            .Bind("punch_right", GamepadInput.Y)
            .Bind("left_trigger", GamepadInput.LeftTrigger)
            .Bind("right_trigger", GamepadInput.RightTrigger)

            // Not on anything until somebody wants them
            .Bind("combo_a_b")
            .Bind("combo_x_y")
            .Bind("combo_a_x")
            .Bind("combo_b_y");
    }

    /// <summary>
    /// Helper method to turn everything a gamepad holds into the buttons of the move files, through its bindings.
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
}
