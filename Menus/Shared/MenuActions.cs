using System;
using System.Collections.Generic;
using System.Text;

using Horizon.Content;
using Horizon.Input;

namespace Fighter2D.Menus;

/// <summary>
/// One thing a gamepad can do on a screen. A button and what it does, the two bumpers as a pair that steps something,
/// the d-pad as what moves, any button at all (joining), or a note that only says something. Each one says when it is
/// live (<see cref="When"/>, for the gamepad asking) and what it is called, which is how the hint under the screen is
/// written out of the same table that handles the presses, so the hint never says something the buttons don't do.
/// </summary>
internal sealed class MenuAction
{
    /// <summary>The button, or the left hand one of a pair. Null for the d-pad, for any button and for a note.</summary>
    public GamepadInput? Button { get; init; }

    /// <summary>The right hand button of a pair, null otherwise.</summary>
    public GamepadInput? Other { get; init; }

    /// <summary>Whether any button at all is this (press any button to join).</summary>
    public bool AnyButton { get; init; }

    /// <summary>Whether this is the d-pad.</summary>
    public bool IsDirections { get; init; }

    /// <summary>What it is called in the hint, a key of the localisation (hints.pick), empty for one that isn't shown.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Whether the hint shows it. A note that is only there to be read is shown and does nothing.</summary>
    public bool Shown { get; init; } = true;

    public Action<Gamepad>? Pressed { get; init; }
    public Action<Gamepad, int>? Stepped { get; init; }
    public Action<Gamepad, int, int>? Moved { get; init; }

    /// <summary>What the hint says for it when that isn't simply the text of its key, worked out when the hint is written.</summary>
    public Func<string>? Says { get; init; }

    /// <summary>Whether it is live for a gamepad right now, always if null.</summary>
    public Func<Gamepad, bool>? When { get; init; }

    public bool IsLive(Gamepad gamepad) => When is null || When(gamepad);
}

/// <summary>
/// The actions of a screen, in the order they are tried in, and the one place every menu reads its gamepads. A screen
/// adds what its buttons do as it is built, <see cref="Update"/> runs the first live action a gamepad pressed (one a
/// tick, whoever pressed first wins, the way the menus always worked) and <see cref="Describe"/> writes the live ones out
/// for the <see cref="HintBar"/>. Which gamepads are listened to is <see cref="Listens"/>, all of them unless a screen says.
/// </summary>
internal sealed class MenuActions
{
    private const string SEPARATOR = "    ";

    // The icons the skin draws a button as, see ButtonGlyphs for how they turn into the ones of whatever pad is held
    private static readonly Dictionary<GamepadInput, string> Icons = new()
    {
        [GamepadInput.A] = "[icon:pad_a]",
        [GamepadInput.B] = "[icon:pad_b]",
        [GamepadInput.X] = "[icon:pad_x]",
        [GamepadInput.Y] = "[icon:pad_y]",
        [GamepadInput.LeftBumper] = "[icon:pad_lb]",
        [GamepadInput.RightBumper] = "[icon:pad_rb]",
        [GamepadInput.LeftTrigger] = "[icon:pad_lt]",
        [GamepadInput.RightTrigger] = "[icon:pad_rt]",
        [GamepadInput.Start] = "[icon:pad_menu]",
        [GamepadInput.Back] = "[icon:pad_view]"
    };

    private const string DPAD_ICON = "[icon:dpad]";

    private readonly List<MenuAction> _actions = [];

    /// <summary>Which gamepads the screen listens to, every connected one if null (the bindings screen listens to one).</summary>
    public Func<Gamepad, bool>? Listens { get; set; }

    /// <summary>Switched off while something else has the buttons (a button being captured, a connection being made).</summary>
    public bool Enabled { get; set; } = true;

    public IReadOnlyList<MenuAction> All => _actions;

    public MenuAction Add(MenuAction action)
    {
        _actions.Add(action);
        return action;
    }

    /// <summary>A button and what it does.</summary>
    public MenuAction Add(GamepadInput button, string text, Action<Gamepad> pressed, Func<Gamepad, bool>? when = null) =>
        Add(new MenuAction { Button = button, Text = text, Pressed = pressed, When = when });

    /// <summary>A button and what it does, for a screen that doesn't care which gamepad.</summary>
    public MenuAction Add(GamepadInput button, string text, Action pressed, Func<bool>? when = null) =>
        Add(new MenuAction { Button = button, Text = text, Pressed = _ => pressed(), When = when is null ? null : _ => when() });

    /// <summary>Two buttons that step something down and up, the bumpers through the tabs.</summary>
    public MenuAction Pair(GamepadInput left, GamepadInput right, string text, Action<Gamepad, int> stepped, Func<Gamepad, bool>? when = null) =>
        Add(new MenuAction { Button = left, Other = right, Text = text, Stepped = stepped, When = when });

    /// <summary>The d-pad (or the stick), told how far right and how far down was pushed, one push a step.</summary>
    public MenuAction Directions(string text, Action<Gamepad, int, int> moved, Func<Gamepad, bool>? when = null) =>
        Add(new MenuAction { IsDirections = true, Text = text, Moved = moved, When = when });

    /// <summary>Any button at all, for joining.</summary>
    public MenuAction Any(string text, Action<Gamepad> pressed, Func<Gamepad, bool>? when = null) =>
        Add(new MenuAction { AnyButton = true, Text = text, Pressed = pressed, When = when });

    /// <summary>Something the hint says without a button to it (waiting for the host).</summary>
    public MenuAction Note(string text, Func<Gamepad, bool>? when = null) =>
        Add(new MenuAction { Text = text, When = when });

    /// <summary>
    /// Reads every gamepad the screen listens to and runs what was pressed, one action a tick.
    /// </summary>
    /// <returns>Whether anything happened.</returns>
    public bool Update()
    {
        if (!Enabled) return false;

        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected || Listens is { } listens && !listens(gamepad)) continue;
            if (Handle(gamepad)) return true;
        }

        return false;
    }

    /// <summary>
    /// Runs the first live action one gamepad pressed, if it pressed one.
    /// </summary>
    public bool Handle(Gamepad gamepad)
    {
        foreach (MenuAction action in _actions)
        {
            if (!action.IsLive(gamepad)) continue;

            if (action.IsDirections)
            {
                int right = MenuInput.Horizontal(gamepad), down = MenuInput.Vertical(gamepad);
                if (right == 0 && down == 0) continue;

                action.Moved?.Invoke(gamepad, right, down);
                return true;
            }

            if (action.AnyButton)
            {
                if (gamepad.Pressed is null) continue;

                action.Pressed?.Invoke(gamepad);
                return true;
            }

            if (action.Button is not { } button) continue;

            if (action.Other is { } other)
            {
                int step = gamepad.WasPressed(other) ? 1 : gamepad.WasPressed(button) ? -1 : 0;
                if (step == 0) continue;

                action.Stepped?.Invoke(gamepad, step);
                return true;
            }

            // A is whatever says yes on the pad (MenuInput knows), the rest are the buttons they are
            bool pressed = button == GamepadInput.A ? MenuInput.ConfirmPressed(gamepad) : gamepad.WasPressed(button);
            if (!pressed) continue;

            action.Pressed?.Invoke(gamepad);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads every gamepad for a question that is up. B is the answer that changes nothing, A takes the one the
    /// navigator is on, and the d-pad walks its buttons.
    /// </summary>
    public static void DriveDialog(Horizon.UI.Components.UIDialog dialog, Horizon.UI.UINavigator navigation)
    {
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            if (gamepad.WasPressed(GamepadInput.B))
            {
                dialog.Cancel();
                return;
            }

            if (MenuInput.ConfirmPressed(gamepad))
            {
                navigation.Activate();
                return;
            }

            int right = MenuInput.Horizontal(gamepad), down = MenuInput.Vertical(gamepad);
            if (right != 0 || down != 0)
            {
                navigation.Move(right, down);
                return;
            }
        }
    }

    /// <summary>
    /// Which actions are live for a gamepad, as a bit each, so the hint knows when to write itself out again.
    /// </summary>
    public ulong Signature(Gamepad? gamepad)
    {
        ulong bits = 0;
        for (int i = 0; i < _actions.Count && i < 64; i++)
        {
            if (_actions[i].Shown && (gamepad is null ? _actions[i].When is null : _actions[i].IsLive(gamepad))) bits |= 1UL << i;
        }
        return bits;
    }

    /// <summary>
    /// Writes the live actions out the way a hint reads, "[icon:dpad] choose    [icon:pad_a] pick", in the icons of an Xbox pad
    /// (<see cref="ButtonGlyphs.Localize"/> turns them into the pad's own).
    /// </summary>
    public void Describe(StringBuilder into, Gamepad? gamepad)
    {
        bool first = true;
        foreach (MenuAction action in _actions)
        {
            if (!action.Shown || (gamepad is null ? action.When is not null : !action.IsLive(gamepad))) continue;

            if (!first) into.Append(SEPARATOR);
            first = false;

            if (action.IsDirections) into.Append(DPAD_ICON).Append(' ');
            else if (action.Button is { } button && Icons.TryGetValue(button, out string? icon))
            {
                into.Append(icon);
                if (action.Other is { } other && Icons.TryGetValue(other, out string? otherIcon)) into.Append(' ').Append(otherIcon);
                into.Append(' ');
            }

            into.Append(action.Says?.Invoke() ?? Localisation.Get(action.Text));
        }
    }
}
