using System.Text;

using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.Menus;

/// <summary>
/// The line under a screen that says which buttons do what, written out of the screen's <see cref="MenuActions"/> for
/// whichever gamepad was touched last, in that pad's icons. It only writes itself out again when the live actions
/// change, and it can say something else for a moment (why a press did nothing) or hold a text of its own (the
/// bindings screen waiting for a button).
/// </summary>
internal sealed class HintBar(Label label, MenuActions actions)
{
    private const float MESSAGE_TIME = 2.5f;

    private readonly StringBuilder _text = new();
    private string _composed = string.Empty;
    private ulong _shown = ulong.MaxValue;
    private GamepadKind? _kind;

    private string _message = string.Empty;
    private float _messageTime;
    private string? _fixed;

    // The colour the layout gave the hint, which it goes back to after a message. Each layout picks one that reads on its
    // own background, the options' is light and the dim menu grey all but vanished on it. Null is the skin's own
    private readonly System.Numerics.Vector4? _colour = label.Color;

    /// <summary>The label it writes into.</summary>
    public Label Label => label;

    /// <summary>
    /// Says something else for a couple of seconds, with a shake. Why something that was pressed did nothing.
    /// </summary>
    public void Say(string message)
    {
        _message = message;
        _messageTime = MESSAGE_TIME;
        label.Shake(6.0f);
    }

    /// <summary>
    /// Holds a text in place of the actions until <see cref="Release"/>, for a screen that is waiting for something.
    /// </summary>
    public void Hold(string text) => _fixed = text;

    public void Release() => _fixed = null;

    /// <summary>
    /// Writes the hint for this update, in the icons of the pad touched last.
    /// </summary>
    public void Update(float dt)
    {
        Gamepad? gamepad = GameInput.Manager.LastUsed;

        if (_messageTime > 0.0f)
        {
            _messageTime -= dt;
            label.Color = MenuColors.Error;
            label.Text = _message;
            return;
        }

        label.Color = _colour;

        if (_fixed is { } held)
        {
            label.Text = ButtonGlyphs.Localize(held, gamepad);
            return;
        }

        ulong live = actions.Signature(gamepad);
        if (live != _shown || gamepad?.Kind != _kind)
        {
            _text.Clear();
            actions.Describe(_text, gamepad);
            _composed = _text.ToString();
            _shown = live;
            _kind = gamepad?.Kind;
        }

        label.Text = ButtonGlyphs.Localize(_composed, gamepad);
    }
}
