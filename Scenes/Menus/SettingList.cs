using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.Scenes;

/// <summary>
/// Rows of settings on a menu, each one a title with a selector next to it. Up and down picks a row, left and right changes what it is set to.
/// The match rules and the options are both one of these.
/// </summary>
internal sealed class SettingList
{
    /// <summary>One row. What it is called, what it is set to, what writes a choice back and what it has to say for itself.</summary>
    private sealed record Setting(Label Title, Selector Selector, Action<int> Apply, string Hint);

    private readonly List<Setting> _settings = [];

    /// <summary>
    /// The row the gamepad is on.
    /// </summary>
    public int Selected { get; private set; }

    /// <summary>
    /// What the row the gamepad is on has to say for itself, empty if it was added without a hint.
    /// </summary>
    public string Hint => _settings.Count > 0 ? _settings[Selected].Hint : string.Empty;

    /// <summary>
    /// Called after any of the settings was changed, by the gamepad or by a click. For whoever wants to save them all.
    /// </summary>
    public Action? Changed { get; set; }

    /// <summary>
    /// Helper method to put a setting on the screen. The layout has to have a selector called <paramref name="name"/> and a label called that with _title after it.
    /// </summary>
    /// <param name="choices">What the setting can be set to, in the order it is stepped through.</param>
    /// <param name="current">What it is set to right now, the first choice is shown if this isn't one of them.</param>
    /// <param name="describe">How a choice is written on the screen.</param>
    /// <param name="apply">Writes a choice down wherever it is kept, called whenever another one is picked.</param>
    /// <param name="hint">A line about the setting, for menus that show one for the row the gamepad is on.</param>
    public void Add<T>(UILayout layout, string name, T[] choices, T current, Func<T, string> describe, Action<T> apply, string hint = "")
    {
        var selector = layout.Get<Selector>(name);
        selector.Options = [.. choices.Select(describe)];
        selector.Index = Math.Max(0, Array.IndexOf(choices, current));

        var setting = new Setting(layout.Get<Label>($"{name}_title"), selector, index => apply(choices[index]), hint);

        // Clicking the selector steps through the choices by itself, all that is left to do is write the new one down
        selector.OnChanged = _ => Apply(setting);

        _settings.Add(setting);
    }

    /// <summary>
    /// Helper method to put a setting on the screen that is either on or off.
    /// </summary>
    public void AddSwitch(UILayout layout, string name, bool current, Action<bool> apply, string hint = "")
    {
        Add(layout, name, [false, true], current, on => on ? "On" : "Off", apply, hint);
    }

    public void Select(int index)
    {
        if (_settings.Count == 0) return;

        Selected = Math.Clamp(index, 0, _settings.Count - 1);

        // The row the gamepad is on is written in white, the others are dimmed
        for (int i = 0; i < _settings.Count; i++)
        {
            _settings[i].Title.Color = i == Selected ? Vector4.One : MenuColors.Hint;
        }
    }

    public void Move(int by)
    {
        if (by != 0) Select(Selected + by);
    }

    /// <summary>
    /// Helper method to set the row the gamepad is on to the choice before or after the one it has, wrapping around at the ends.
    /// </summary>
    public void Step(int direction)
    {
        if (_settings.Count == 0 || direction == 0) return;

        Setting setting = _settings[Selected];

        int count = setting.Selector.Options.Length;
        if (count < 2) return;

        setting.Selector.Index = (setting.Selector.Index + direction + count) % count;
        setting.Selector.Punch(0.08f, 0.2f);

        Apply(setting);
    }

    private void Apply(Setting setting)
    {
        setting.Apply(setting.Selector.Index);
        Changed?.Invoke();
    }
}
