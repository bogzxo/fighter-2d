using Horizon.Content;
using System.Collections.Generic;
using System.Text;

using Horizon.Input;

namespace Fighter2D.Input;

/// <summary>
/// Turns buttons into text with icons in it, for labels. The UI skin draws [icon:pad_a] as the A button and so on, out of the
/// gamepad art of the Dead Revolver pack. All texts are written with the icons of an Xbox gamepad, <see cref="Localize"/> has the
/// skin swap them for the PlayStation ones when that is what the player is holding (icon_sets in skin.hor says which is which).
/// </summary>
internal static class ButtonGlyphs
{
    // What a text starts with to be shown with the icons of a PlayStation gamepad. Which icons those are is up to the skin (icon_sets in skin.hor)
    private const string PLAYSTATION_ICONS = "[icons:playstation]";

    // The same for a keyboard, whose icons are its keys. A keyboard has no d-pad to draw, it has four arrows
    private const string KEYBOARD_ICONS = "[icons:keyboard]";
    private const string DPAD_ICON = "[icon:dpad]";
    private const string ARROW_KEYS = "[icon:key_left][icon:key_right][icon:key_up][icon:key_down]";

    // Hints are asked for on every update, so every text is only ever translated once
    private static readonly Dictionary<string, string> playStationTexts = [];
    private static readonly Dictionary<string, string> keyboardTexts = [];

    // The same for what is held, which the input display asks for every line on every tick a button is held. There
    // aren't that many ways to mash a pad, and building them anew every time was a pile of garbage a second for nowt
    private static readonly Dictionary<InputFlags, string> heldTexts = [];

    // The buttons of the move files and their icons, in the order they are written out in. Directions first, like every input display ever
    private static readonly (InputFlags Flag, string Icon)[] FightIcons =
    [
        (InputFlags.DPadLeft, "[icon:left]"),
        (InputFlags.DPadRight, "[icon:right]"),
        (InputFlags.DPadUp, "[icon:up]"),
        (InputFlags.DPadDown, "[icon:down]"),
        (InputFlags.A, "[icon:pad_a]"),
        (InputFlags.B, "[icon:pad_b]"),
        (InputFlags.X, "[icon:pad_x]"),
        (InputFlags.Y, "[icon:pad_y]"),
        (InputFlags.LeftBumper, "[icon:pad_lb]"),
        (InputFlags.RightBumper, "[icon:pad_rb]"),
        (InputFlags.LeftTrigger, "[icon:pad_lt]"),
        (InputFlags.RightTrigger, "[icon:pad_rt]"),
    ];

    /// <summary>
    /// Helper method to make a text show the icons of the right gamepad.
    /// </summary>
    /// <param name="gamepad">The gamepad the text is for, null leaves the text as it is.</param>
    public static string Localize(string text, Gamepad? gamepad)
    {
        if (gamepad?.Kind == GamepadKind.Keyboard)
        {
            lock (keyboardTexts)
            {
                if (keyboardTexts.TryGetValue(text, out string? keys)) return keys;

                return keyboardTexts[text] = KEYBOARD_ICONS + text.Replace(DPAD_ICON, ARROW_KEYS);
            }
        }

        if (gamepad?.Kind != GamepadKind.PlayStation) return text;

        lock (playStationTexts)
        {
            if (playStationTexts.TryGetValue(text, out string? known)) return known;

            return playStationTexts[text] = PLAYSTATION_ICONS + text;
        }
    }

    // The same for a keyboard, whose buttons are written as the keys they are on. Forgotten when the bindings change
    private static readonly Dictionary<InputFlags, string> heldKeyTexts = [];

    /// <summary>
    /// Forgets what was worked out of the bindings, for when they have changed (see GameInput.Save).
    /// </summary>
    public static void Forget()
    {
        lock (heldKeyTexts) heldKeyTexts.Clear();
    }

    /// <summary>
    /// Helper method to write out the buttons of the move files that are held together the way the gamepad that holds
    /// them has them. A keyboard has keys for them, the J its left kick is on and not the space bar the icon of the
    /// bottom button is on a keyboard. One keyboard plays at a time, so its texts are kept by what is held alone.
    /// </summary>
    public static string Describe(InputFlags held, Gamepad? gamepad)
    {
        if (gamepad?.Kind != GamepadKind.Keyboard || held == InputFlags.None) return Describe(held);

        lock (heldKeyTexts)
        {
            if (heldKeyTexts.TryGetValue(held, out string? known)) return known;

            var text = new StringBuilder();
            foreach (var (flag, icon) in FightIcons)
            {
                if ((held & flag) == 0) continue;
                text.Append(KeyOf(gamepad, flag) ?? icon);
            }

            return heldKeyTexts[held] = text.ToString();
        }
    }

    // The key the action of a button of the move files is on first, null for one on no key (or a direction, which the arrows say)
    private static string? KeyOf(Gamepad gamepad, InputFlags flag)
    {
        if ((flag & (InputFlags.DPadLeft | InputFlags.DPadRight | InputFlags.DPadUp | InputFlags.DPadDown)) != 0) return null;

        foreach (var action in GameInput.Actions)
        {
            if (action.Flag != flag) continue;

            var keys = gamepad.Bindings.KeyCombinationsOf(action.Name);
            return keys.Count > 0 ? Describe(keys[0]) : null;
        }

        return null;
    }

    /// <summary>
    /// Helper method to write out the buttons of the move files that are held together, as icons. Empty if nothing is held.
    /// </summary>
    public static string Describe(InputFlags held)
    {
        if (held == InputFlags.None) return string.Empty;

        lock (heldTexts)
        {
            if (heldTexts.TryGetValue(held, out string? known)) return known;

            var text = new StringBuilder();
            foreach (var (flag, icon) in FightIcons)
            {
                if ((held & flag) != 0) text.Append(icon);
            }

            return heldTexts[held] = text.ToString();
        }
    }

    /// <summary>
    /// Helper method to write out the first thing an action is bound to on a gamepad, as icons (the keys first on a
    /// keyboard), null when it is on nothing. For a card of the controls, where "or the left stick" is room nobody has.
    /// Not localized, see <see cref="Localize"/>.
    /// </summary>
    public static string? FirstOf(Gamepad gamepad, string action)
    {
        IReadOnlyList<Silk.NET.Input.Key[]> keys = gamepad.Bindings.KeyCombinationsOf(action);
        if (gamepad.Kind == GamepadKind.Keyboard && keys.Count > 0) return Describe(keys[0]);

        IReadOnlyList<uint> buttons = gamepad.Bindings.CombinationsOf(action);
        if (buttons.Count > 0) return Describe(buttons[0]);

        return keys.Count > 0 ? Describe(keys[0]) : null;
    }

    /// <summary>
    /// Helper method to write out what an action is bound to on a gamepad.
    /// </summary>
    public static string Describe(Gamepad gamepad, string action) => Localize(Describe(gamepad.Bindings, action), gamepad);

    /// <summary>
    /// Helper method to write out buttons that are held together on a gamepad.
    /// </summary>
    public static string Describe(Gamepad gamepad, uint combination) => Localize(Describe(combination), gamepad);

    /// <summary>
    /// Helper method to write out everything an action is bound to, with the icons of an Xbox gamepad.
    /// </summary>
    public static string Describe(GamepadBindings bindings, string action)
    {
        var text = new StringBuilder();

        foreach (uint combination in bindings.CombinationsOf(action))
        {
            if (text.Length > 0) text.Append(Localisation.Get("common.or"));
            text.Append(Describe(combination));
        }

        foreach (Silk.NET.Input.Key[] keys in bindings.KeyCombinationsOf(action))
        {
            if (text.Length > 0) text.Append(Localisation.Get("common.or"));
            text.Append(Describe(keys));
        }

        return text.Length > 0 ? text.ToString() : Localisation.Get("common.not_bound");
    }

    /// <summary>
    /// Helper method to write out physical buttons that are held together.
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
    /// Helper method to get the icon of a physical button.
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
        GamepadInput.Start => "[icon:pad_menu]",
        GamepadInput.Back => "[icon:pad_view]",
        GamepadInput.Home => "[icon:pad_home]",
        GamepadInput.LeftStick => "[icon:pad_ls]",
        GamepadInput.RightStick => "[icon:pad_rs]",
        GamepadInput.DPadUp => "[icon:dpad_up]",
        GamepadInput.DPadDown => "[icon:dpad_down]",
        GamepadInput.DPadLeft => "[icon:dpad_left]",
        GamepadInput.DPadRight => "[icon:dpad_right]",
        GamepadInput.LeftStickUp => Stick("[icon:pad_ls]", "bindings.stick_up"),
        GamepadInput.LeftStickDown => Stick("[icon:pad_ls]", "bindings.stick_down"),
        GamepadInput.LeftStickLeft => Stick("[icon:pad_ls]", "bindings.stick_left"),
        GamepadInput.LeftStickRight => Stick("[icon:pad_ls]", "bindings.stick_right"),
        GamepadInput.RightStickUp => Stick("[icon:pad_rs]", "bindings.stick_up"),
        GamepadInput.RightStickDown => Stick("[icon:pad_rs]", "bindings.stick_down"),
        GamepadInput.RightStickLeft => Stick("[icon:pad_rs]", "bindings.stick_left"),
        GamepadInput.RightStickRight => Stick("[icon:pad_rs]", "bindings.stick_right"),
        _ => input.ToString()
    };

    // The keys the skins have a picture of (the ones the menus are on), by the name of the picture
    private static readonly Dictionary<Silk.NET.Input.Key, string> KeyIcons = new()
    {
        [Silk.NET.Input.Key.A] = "key_a", [Silk.NET.Input.Key.C] = "key_c", [Silk.NET.Input.Key.D] = "key_d",
        [Silk.NET.Input.Key.E] = "key_e", [Silk.NET.Input.Key.Q] = "key_q", [Silk.NET.Input.Key.R] = "key_r",
        [Silk.NET.Input.Key.S] = "key_s", [Silk.NET.Input.Key.W] = "key_w", [Silk.NET.Input.Key.Z] = "key_z",
        [Silk.NET.Input.Key.Up] = "key_up", [Silk.NET.Input.Key.Down] = "key_down", [Silk.NET.Input.Key.Left] = "key_left", [Silk.NET.Input.Key.Right] = "key_right",
        [Silk.NET.Input.Key.Space] = "key_space", [Silk.NET.Input.Key.Enter] = "key_enter", [Silk.NET.Input.Key.Escape] = "key_esc",
        [Silk.NET.Input.Key.Tab] = "key_tab", [Silk.NET.Input.Key.Backspace] = "key_backspace",
        [Silk.NET.Input.Key.ShiftLeft] = "key_shift", [Silk.NET.Input.Key.ControlLeft] = "key_ctrl"
    };

    /// <summary>
    /// Helper method to write out keys held together, as the pictures of them where the skin has one and the name of the
    /// key in bold where it hasn't (J, semicolon).
    /// </summary>
    public static string Describe(IReadOnlyList<Silk.NET.Input.Key> keys)
    {
        var text = new StringBuilder();
        foreach (Silk.NET.Input.Key key in keys)
        {
            if (text.Length > 0) text.Append(" + ");
            if (KeyIcons.TryGetValue(key, out string? icon)) text.Append("[icon:").Append(icon).Append(']');
            else text.Append("[b]").Append(key).Append("[/b]");
        }
        return text.ToString();
    }

    // A stick pushed one way, the icon of the stick put wherever the language puts it
    private static string Stick(string icon, string key) => Localisation.Format(key, ("stick", icon));
}
