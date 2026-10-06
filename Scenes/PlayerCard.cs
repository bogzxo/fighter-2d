using System.Numerics;

using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.Scenes;

/// <summary>
/// The card of one player on the gamepad select screen. It has a title, a status line and a line of detail under that.
/// The card itself comes out of Assets/ui/layouts/player_card.hor.
/// </summary>
internal sealed class PlayerCard
{
    private const int MAX_NAME_LENGTH = 22;

    public StackPanel Box { get; }

    private readonly Label _status, _detail;

    public PlayerCard(UILayout item, string title)
    {
        item.Get<Label>("title").Text = title;

        Box = item.Get<StackPanel>("box");
        _status = item.Get<Label>("status");
        _detail = item.Get<Label>("detail");
    }

    public void ShowStatus(string text, Vector4 color)
    {
        _status.Text = text;
        _status.Color = color;
    }

    public void ShowDetail(string text, Vector4 color)
    {
        _detail.Text = text;
        _detail.Color = color;
    }

    /// <summary>
    /// Helper method to give the card a little jump, so there is no doubt which one something just happened to.
    /// </summary>
    public void Bump(float amount = 0.1f, float duration = 0.3f) => Box.Punch(amount, duration);

    /// <summary>
    /// Helper method to cut the name of a gamepad short, some of them have a lot to say about themselves.
    /// </summary>
    public static string Shorten(string name)
    {
        if (name.Length == 0) return "Gamepad";

        return name.Length <= MAX_NAME_LENGTH ? name : name[..(MAX_NAME_LENGTH - 2)] + "..";
    }
}
