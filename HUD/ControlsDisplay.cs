using System;
using System.Collections.Generic;
using System.Text;

using Horizon.Content;
using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The card of the controls, in the bottom corner on a player's side. What every action of the fight is on, read off that
/// player's own bindings (a keyboard says its keys, a rebound button says where it went), and under them how to get the
/// lab drawer out of a practice fight and how to pause. Only for somebody holding a gamepad on this machine, the dummy and
/// the other end of an online fight have nothing to show. It slides down out of the way while the drawer is out, the pause
/// is up, or the fight isn't on (the versus screen, the result), and is written again every time it comes back up, the
/// bindings might have changed under the options in the meantime. The cards are in fight_overlay.hor.
/// </summary>
internal sealed class ControlsDisplay : IHudDisplay
{
    // Where the lab drawer and the pause are on a keyboard. Backspace is the keyboard's back button, escape pauses a fight
    private const string KEYBOARD_DRAWER = "[icon:key_backspace]";
    private const string KEYBOARD_PAUSE = "[icon:key_esc]";

    // The actions written out, the two ways across as one line first
    private static readonly string[] Listed =
    [
        "jump", "crouch", "block",
        "kick_left", "kick_right", "punch_left", "punch_right",
        "left_bumper", "left_trigger", "right_trigger",
        "combo_a_b", "combo_x_y", "combo_a_x", "combo_b_y"
    ];

    private sealed class Card
    {
        public required UIComponent Root;
        public required Label Left, Right, Hint;
        public bool Up;
    }

    private readonly RoundDirector _round;
    private readonly bool _practice;
    private readonly Func<bool>? _hidden;
    private readonly Card[] _cards;
    private readonly List<string> _lines = [];
    private readonly StringBuilder _text = new();

    /// <summary>Whether the cards are wanted at all, see GameOptions.Controls and the lab drawer.</summary>
    public bool Wanted { get; set; }

    public ControlsDisplay(UILayout layout, RoundDirector round, bool practice, Func<bool>? hidden)
    {
        _round = round;
        _practice = practice;
        _hidden = hidden;
        _cards = [Find(layout, "player1"), Find(layout, "player2")];
    }

    private static Card Find(UILayout layout, string player)
    {
        var card = new Card
        {
            Root = layout.Get<UIComponent>(player + "_controls"),
            Left = layout.Get<Label>(player + "_controls_left"),
            Right = layout.Get<Label>(player + "_controls_right"),
            Hint = layout.Get<Label>(player + "_controls_hint")
        };

        // Down out of sight to begin with, the versus screen comes first anyway
        card.Root.Show(false, instantly: true);
        return card;
    }

    public void Update(float dt)
    {
        bool on = Wanted && _hidden?.Invoke() != true && _round.Phase is RoundPhase.Ready or RoundPhase.Fight or RoundPhase.RoundOver;

        for (int i = 0; i < _cards.Length; i++)
        {
            Card card = _cards[i];
            Gamepad? gamepad = _round.PlayerOf(i).Controller?.Input.Gamepad;
            bool up = on && gamepad is { IsConnected: true };

            if (up == card.Up) continue;
            card.Up = up;

            if (up) Write(card, gamepad!, drawer: _practice && i == 0);
            card.Root.Show(up);
        }
    }

    // Fills a card in for whoever holds the gamepad, half the actions down each column
    private void Write(Card card, Gamepad gamepad, bool drawer)
    {
        _lines.Clear();

        string? left = ButtonGlyphs.FirstOf(gamepad, "move_left"), right = ButtonGlyphs.FirstOf(gamepad, "move_right");
        if (left is not null || right is not null) _lines.Add(Line(left + right, Localisation.Get("controls.move")));

        foreach (string action in Listed)
        {
            // Whatever is on nothing (the combos until somebody wants them) isn't a control
            if (ButtonGlyphs.FirstOf(gamepad, action) is { } button)
                _lines.Add(Line(button, Localisation.Get("bindings." + action)));
        }

        int half = (_lines.Count + 1) / 2;
        card.Left.Text = ButtonGlyphs.Localize(Join(0, half), gamepad);
        card.Right.Text = ButtonGlyphs.Localize(Join(half, _lines.Count), gamepad);

        bool keyboard = gamepad.Kind == GamepadKind.Keyboard;
        _text.Clear();
        if (drawer)
        {
            _text.Append(Line(keyboard ? KEYBOARD_DRAWER : ButtonGlyphs.Describe(GamepadInput.Back), Localisation.Get("controls.drawer")));
            _text.Append("      ");
        }
        _text.Append(Line(keyboard ? KEYBOARD_PAUSE : ButtonGlyphs.Describe(GamepadInput.Start), Localisation.Get("controls.pause")));
        card.Hint.Text = ButtonGlyphs.Localize(_text.ToString(), gamepad);
    }

    private static string Line(string button, string action) => Localisation.Format("controls.line", ("button", button), ("action", action));

    private string Join(int from, int to)
    {
        _text.Clear();
        for (int i = from; i < to; i++)
        {
            if (i > from) _text.Append('\n');
            _text.Append(_lines[i]);
        }
        return _text.ToString();
    }
}
