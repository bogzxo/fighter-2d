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

    // Hints are asked for on every update, so every text is only ever translated once
    private static readonly Dictionary<string, string> playStationTexts = [];

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
        if (gamepad?.Kind != GamepadKind.PlayStation) return text;

        lock (playStationTexts)
        {
            if (playStationTexts.TryGetValue(text, out string? known)) return known;

            return playStationTexts[text] = PLAYSTATION_ICONS + text;
        }
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
            if (text.Length > 0) text.Append("  or  ");
            text.Append(Describe(combination));
        }

        return text.Length > 0 ? text.ToString() : "not bound";
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
