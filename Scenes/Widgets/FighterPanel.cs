using System.Numerics;

using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.Scenes.Widgets;

/// <summary>
/// One side of the fight setup screen. Who is in that corner, drawn big with their name and their stat bars, and under
/// that whoever is going to play them and what they are up to, in a status line and a line of detail.
/// The panel itself comes out of Assets/ui/layouts/fighter_panel.hor.
/// </summary>
internal sealed class FighterPanel
{
    private const string ANIMATION = "run_loop";
    private const int MAX_NAME_LENGTH = 22;

    public StackPanel Box { get; }

    private readonly CharacterPortrait _portrait;
    private readonly Label _title, _name, _status, _detail;
    private readonly ProgressBar _speed, _health, _bulk, _complexity;

    // Who is being shown right now, so we only switch when it actually changes. Nobody yet, and not even the empty corner
    private CharacterDefinition? _shown;
    private bool _empty;

    /// <param name="mirrored">Whether the fighter looks to the left, which whoever is on the right of the screen does.</param>
    public FighterPanel(UILayout item, bool mirrored)
    {
        Box = item.Get<StackPanel>("box");
        _portrait = new CharacterPortrait(item.Get<Image>("portrait")) { Mirrored = mirrored };
        _title = item.Get<Label>("title");
        _name = item.Get<Label>("name");
        _status = item.Get<Label>("status");
        _detail = item.Get<Label>("detail");
        _speed = item.Get<ProgressBar>("speed");
        _health = item.Get<ProgressBar>("health");
        _bulk = item.Get<ProgressBar>("bulk");
        _complexity = item.Get<ProgressBar>("complexity");

        Show(null);
    }

    public void ShowTitle(string text, Vector4 color)
    {
        _title.Text = text;
        _title.Color = color;
    }

    /// <summary>
    /// Shows a fighter in the corner, or nobody at all for a corner that is still waiting for somebody to turn up.
    /// </summary>
    public void Show(CharacterDefinition? character)
    {
        if (character is null)
        {
            if (_empty) return;

            _shown = null;
            _empty = true;
            // Nothing in it, but its room is kept so the panel is the same size with somebody in the corner or without
            _portrait.Image.Atlas = null;
            _name.Text = "?";
            _speed.Progress = _health.Progress = _bulk.Progress = _complexity.Progress = 0.0f;
            return;
        }

        if (ReferenceEquals(character, _shown)) return;

        _shown = character;
        _empty = false;

        _portrait.Show(character, ANIMATION);
        _portrait.Image.Punch(0.06f, 0.25f);

        _name.Text = character.PrettyName;
        _speed.Progress = character.Speed;
        _health.Progress = character.Health;
        _bulk.Progress = character.Bulk;
        _complexity.Progress = character.Complexity;
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
    /// Helper method to give the panel a little jump, so there is no doubt which one something just happened to.
    /// </summary>
    public void Bump(float amount = 0.06f, float duration = 0.3f) => Box.Punch(amount, duration);

    public void Update(float dt) => _portrait.Update(dt);

    /// <summary>
    /// Helper method to cut the name of a gamepad short, some of them have a lot to say about themselves.
    /// </summary>
    public static string Shorten(string name)
    {
        if (name.Length == 0) return "Gamepad";

        return name.Length <= MAX_NAME_LENGTH ? name : name[..(MAX_NAME_LENGTH - 2)] + "..";
    }
}
