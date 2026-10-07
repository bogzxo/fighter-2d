using System.Collections.Generic;
using System.Text;

using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The input display, like the one in the training mode of every fighting game. A list of what each player held lately down the side of the screen,
/// newest on top, with how many ticks each set of buttons was held for. It shows everybody the same way, be it a gamepad, the dummy or a network player.
/// What a player held is logged by their controller (see PlayerInputs.Log), this only draws it.
/// </summary>
internal sealed class InputDisplay : IHudDisplay
{
    // How many lines of each list are shown
    private const int LINES = 10;

    // What a line shows when nothing is held, a line of nothing would make the list look like it has holes in it
    private const string NEUTRAL = "-";

    private readonly RoundDirector _round;
    private readonly Label[] _lists;
    private readonly StringBuilder _text = new();

    // The newest line of each list as it was last written, the list is only written again once that changes
    private readonly (int Count, InputLogEntry Newest)[] _shown = new (int, InputLogEntry)[2];

    public InputDisplay(UILayout layout, RoundDirector round)
    {
        _round = round;
        _lists = [layout.Get<Label>("player1_inputs"), layout.Get<Label>("player2_inputs")];

        foreach (Label list in _lists) list.Text = string.Empty;
    }

    public void Update(float dt)
    {
        for (int i = 0; i < _lists.Length; i++)
        {
            Player player = _round.PlayerOf(i);
            if (player.Controller?.Inputs is not { } inputs) continue;

            IReadOnlyList<InputLogEntry> log = inputs.Log;
            var newest = (log.Count, log.Count > 0 ? log[0] : default);

            if (newest == _shown[i]) continue;
            _shown[i] = newest;

            // The icons are the ones of the gamepad the player is holding, if they are holding one
            _lists[i].Text = ButtonGlyphs.Localize(Write(log, mirrored: i == 1), player.Controller.Input.Gamepad);
        }
    }

    /// <summary>
    /// Helper method to write a log out as lines of text, with the tick count on the outside of the screen and the buttons towards the middle.
    /// </summary>
    private string Write(IReadOnlyList<InputLogEntry> log, bool mirrored)
    {
        _text.Clear();

        for (int i = 0; i < log.Count && i < LINES; i++)
        {
            if (i > 0) _text.Append('\n');

            string buttons = ButtonGlyphs.Describe(log[i].Held);
            if (buttons.Length == 0) buttons = NEUTRAL;

            if (mirrored) _text.Append(buttons).Append("  ").Append(log[i].Ticks);
            else _text.Append(log[i].Ticks).Append("  ").Append(buttons);
        }

        return _text.ToString();
    }
}
