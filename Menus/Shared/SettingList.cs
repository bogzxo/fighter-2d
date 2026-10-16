using Horizon.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.Menus;

/// <summary>
/// Rows of settings on a menu, each one a title with a selector next to it. Up and down picks a row, left and right changes what it is set to.
/// The match rules and the options are both one of these.
/// The rows go up and down in the order the layout has them in, not the order the code added them in, so moving a
/// row in Hex is all it takes to move it for the gamepad as well.
/// </summary>
internal sealed class SettingList
{
    /// <summary>One row. What it is called, what it is set to, what writes a choice back and what it has to say for itself.</summary>
    private sealed record Setting(string Name, Label Title, Selector Selector, Action<int> Apply, string Hint);

    // What the row the gamepad is on is laid over, when the layout made its rows panels. Barely there, it only has to be findable
    private static readonly Vector4 RowHighlight = new(0.71f, 0.59f, 0.94f, 0.16f);

    private readonly List<Setting> _settings = [];

    public int Count => _settings.Count;

    /// <summary>
    /// The row the gamepad is on.
    /// </summary>
    public int Selected { get; private set; }

    /// <summary>
    /// What the row the gamepad is on has to say for itself, empty if it was added without a hint.
    /// </summary>
    public string Hint => _settings.Count > 0 ? _settings[Selected].Hint : string.Empty;

    /// <summary>
    /// What the row the gamepad is on is called, as its title in the layout has it.
    /// </summary>
    public string Title => _settings.Count > 0 ? _settings[Selected].Title.Text : string.Empty;

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

        var setting = new Setting(name, layout.Get<Label>($"{name}_title"), selector, index => apply(choices[index]), hint);

        // Clicking the selector steps through the choices by itself, all that is left to do is write the new one down
        selector.OnChanged = _ => Apply(setting);

        // In before the first row that comes after it in the layout, so the list is always in the layout's order
        int at = _settings.Count;
        while (at > 0 && ComesBefore(selector, _settings[at - 1].Selector)) at--;
        _settings.Insert(at, setting);
    }

    /// <summary>
    /// Which row a setting is, by the name it was added under. -1 for one that wasn't, for a shortcut to a row that
    /// shouldn't care where in the list the layout put it.
    /// </summary>
    public int IndexOf(string name) => _settings.FindIndex(setting => setting.Name == name);

    /// <summary>
    /// Helper method to tell whether one component comes before another in the layout, going through it from the top
    /// the way it is written. Where their branches part, whichever branch is the earlier child goes first.
    /// </summary>
    private static bool ComesBefore(UIComponent a, UIComponent b)
    {
        List<UIComponent> fromA = Branch(a), fromB = Branch(b);

        // The two lists start at the root, the first place they differ is where the branches part
        int shared = 0;
        while (shared < fromA.Count && shared < fromB.Count && fromA[shared] == fromB[shared]) shared++;

        // One inside the other, the outer one comes first
        if (shared == fromA.Count) return true;
        if (shared == fromB.Count || shared == 0) return false;

        IReadOnlyList<UIComponent> siblings = fromA[shared - 1].Children;
        return IndexIn(siblings, fromA[shared]) < IndexIn(siblings, fromB[shared]);
    }

    private static List<UIComponent> Branch(UIComponent component)
    {
        var branch = new List<UIComponent>();
        for (UIComponent? at = component; at is not null; at = at.Parent) branch.Add(at);
        branch.Reverse();
        return branch;
    }

    private static int IndexIn(IReadOnlyList<UIComponent> siblings, UIComponent child)
    {
        for (int i = 0; i < siblings.Count; i++)
        {
            if (siblings[i] == child) return i;
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Helper method to put a setting on the screen that is either on or off.
    /// </summary>
    public void AddSwitch(UILayout layout, string name, bool current, Action<bool> apply, string hint = "")
    {
        Add(layout, name, [false, true], current, on => Localisation.Get(on ? "common.on" : "common.off"), apply, hint);
    }

    public void Select(int index)
    {
        if (_settings.Count == 0) return;

        Selected = Math.Clamp(index, 0, _settings.Count - 1);
        Light(Selected);
    }

    /// <summary>
    /// Helper method to dim every row, for a list the gamepad isn't on at all right now. Where it was is remembered.
    /// </summary>
    public void Dim() => Light(-1);

    /// <summary>
    /// The row the gamepad is on is written in white with a wash of colour behind it, the others are dimmed. It does
    /// what the layout says it does under the pointer as well (the options rows nudge along), held on for as long
    /// as the pad is on it.
    /// </summary>
    private void Light(int lit)
    {
        for (int i = 0; i < _settings.Count; i++)
        {
            _settings[i].Title.Color = i == lit ? Vector4.One : MenuColors.Hint;
            if (_settings[i].Title.Parent is not Panel row) continue;

            row.Color = i == lit ? RowHighlight : null;
            row.HoverHeld = i == lit;
        }
    }

    public void Move(int by)
    {
        if (by != 0) Select(Selected + by);
    }

    /// <summary>
    /// Helper method to set the row the gamepad is on to the choice before or after the one it has, wrapping around at the ends.
    /// </summary>
    public void Step(int direction) => Step(Selected, direction);

    /// <summary>
    /// Helper method to step a row whether the gamepad is on it or not, for a shortcut to one of them (the bumpers and the map).
    /// </summary>
    public void Step(int row, int direction)
    {
        if (row < 0 || row >= _settings.Count || direction == 0) return;

        Setting setting = _settings[row];

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
