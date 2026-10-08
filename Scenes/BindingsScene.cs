using System;
using System.Collections.Generic;

using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the bindings of one gamepad can be changed.
/// Leaving the scene saves the bindings of every gamepad, see <see cref="GameInput.Save"/>.
/// </summary>
internal class BindingsScene(int slot, MatchSetup setup) : MenuScene
{
    private const string HINT_BROWSE = "[icon:dpad] choose    [icon:pad_a] change    [icon:pad_x] add another    [icon:pad_y] clear    [icon:pad_b] done";
    private const string TEXT_LISTENING = "press a button...";

    protected override string LayoutFile => MenuLayouts.BINDINGS;

    // The rows of the actions come first, the two buttons underneath them (defaults and done) come last
    private readonly ButtonList _entries = new();
    private readonly BindingCapture _capture = new();
    private Label _hint = null!;
    private Gamepad? _gamepad;

    // The actions are spread over the columns of the layout, the next column starts where the one before it is full
    private int _rowsPerColumn = 1;

    private static int ActionCount => GameInput.Actions.Length;
    private static int DefaultsIndex => ActionCount;
    private static int DoneIndex => ActionCount + 1;

    protected override void Prepare()
    {
        // The selector only sends us here for a gamepad that exists
        GameInput.Manager.TryGet(slot, out _gamepad);
    }

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/bindings.hor, the row of an action in binding_row.hor.
        // The player is whoever picked this gamepad, which is not the same thing as the slot it is plugged into
        int player = Math.Max(0, setup.Slots.IndexOf(slot));
        layout.Get<Label>("title").Text = $"Bindings of player {player + 1}";
        layout.Get<Label>("gamepad_name").Text = _gamepad?.Name ?? string.Empty;

        BuildRows(layout);

        AddEntry(layout.Get<Button>("btn_defaults"), DefaultsIndex);
        AddEntry(layout.Get<Button>("btn_done"), DoneIndex);

        _hint = layout.Get<Label>("hint");

        Refresh();
        _entries.Select(0);
    }

    /// <summary>
    /// Helper method to make a row for every action, shared out over however many columns the layout has.
    /// </summary>
    private void BuildRows(UILayout layout)
    {
        var columns = new List<StackPanel>();
        while (layout.TryGet($"column_{columns.Count + 1}", out StackPanel? column))
        {
            columns.Add(column);
        }

        if (columns.Count == 0) columns.Add(layout.Get<StackPanel>("column_1"));

        _rowsPerColumn = (ActionCount + columns.Count - 1) / columns.Count;

        for (int c = 0; c < columns.Count; c++)
        {
            int first = c * _rowsPerColumn;
            var actions = GameInput.Actions.Skip(first).Take(Math.Clamp(ActionCount - first, 0, _rowsPerColumn)).ToList();

            layout.Populate(columns[c], actions, (row, action, i) =>
            {
                row.Get<Label>("action").Text = action.Label;

                // Clicking a row does what A does on it, the button to bind still has to come from the gamepad
                AddEntry(row.Get<Button>("binding"), first + i);
            });
        }
    }

    private void AddEntry(Button button, int index)
    {
        button.OnPressed = () => Activate(index, adding: false);
        _entries.Add(button);
    }

    protected override void UpdateMenu(float dt)
    {
        // Nothing to bind on a gamepad that has been pulled out
        if (_gamepad is not { IsConnected: true })
        {
            Leave();
            return;
        }

        if (!InputReady) return;

        if (_capture.IsListening)
        {
            UpdateCapture(dt);
            return;
        }

        _entries.Move(MenuInput.Vertical(_gamepad));

        int across = MenuInput.Horizontal(_gamepad);
        if (across != 0) SelectAcross(across);

        if (_gamepad.WasPressed(GamepadInput.A)) Activate(_entries.Selected, adding: false);
        else if (_gamepad.WasPressed(GamepadInput.X)) Activate(_entries.Selected, adding: true);
        else if (_gamepad.WasPressed(GamepadInput.Y)) Clear(_entries.Selected);
        else if (_gamepad.WasPressed(GamepadInput.B)) Leave();
    }

    private void UpdateCapture(float dt)
    {
        int row = _capture.Row;

        switch (_capture.Update(dt, _gamepad!.HeldMask))
        {
            case CaptureState.Changed:
                _entries[row].Label = ButtonGlyphs.Describe(_gamepad, _capture.Buttons);
                break;

            case CaptureState.Captured:
                Bind(GameInput.Actions[row].Name, _capture.Buttons, _capture.Adding);
                _entries[row].Punch(0.06f, 0.3f);
                StopCapture();

                // On to the next action, so a whole column can be set by pressing A and a button over and over
                if (row + 1 < ActionCount) _entries.Select(row + 1);
                break;

            case CaptureState.Cancelled:
                StopCapture();
                break;
        }
    }

    private void Bind(string action, uint buttons, bool adding)
    {
        if (adding)
        {
            _gamepad!.Bindings.AddCombination(action, buttons);
            return;
        }

        // Whatever else was on exactly these buttons loses them, the same buttons doing two things is never what you want
        _gamepad!.Bindings.Rebind(action, buttons);
    }

    /// <summary>
    /// Helper method to take an action off every button it is on. The action stays and can be put back on one.
    /// </summary>
    private void Clear(int index)
    {
        if (index >= ActionCount) return;

        _gamepad!.Bindings.Bind(GameInput.Actions[index].Name);
        _entries[index].Shake(5.0f, 0.2f);
        Refresh();
    }

    /// <summary>
    /// Called for the entry that was clicked, or that A or X was pressed on.
    /// </summary>
    private void Activate(int index, bool adding)
    {
        if (_gamepad is null) return;

        // A click can land on an entry while another one is still waiting for its button
        if (_capture.IsListening) StopCapture();

        _entries.Select(index);

        if (index == DefaultsIndex) ResetToDefaults();
        else if (index == DoneIndex) Leave();
        else StartCapture(index, adding);
    }

    private void StartCapture(int index, bool adding)
    {
        _capture.Start(index, adding);

        _entries[index].Label = TEXT_LISTENING;
        _hint.Text = ButtonGlyphs.Localize($"Press the button for {GameInput.Actions[index].Label.ToLowerInvariant()}, or several together    [icon:pad_menu] cancels", _gamepad);
    }

    private void StopCapture()
    {
        _capture.Stop();
        Refresh();
    }

    private void ResetToDefaults()
    {
        _gamepad!.Bindings.CopyFrom(GameInput.CreateDefaultBindings());
        Refresh();

        // Every row gives a nod one after the other, they have all just changed
        for (int i = 0; i < ActionCount; i++)
        {
            _entries[i].Punch(0.04f, 0.25f).SetDelay((i % _rowsPerColumn) * 0.03f);
        }
    }

    private void Leave()
    {
        GameInput.Save();
        GoTo(new GamepadSelectorScene(setup));
    }

    /// <summary>
    /// Helper method to step sideways, to the same row of the next column or between the two buttons underneath.
    /// </summary>
    private void SelectAcross(int direction)
    {
        if (_entries.Selected >= DefaultsIndex)
        {
            _entries.Select(direction > 0 ? DoneIndex : DefaultsIndex);
            return;
        }

        int target = _entries.Selected + direction * _rowsPerColumn;
        if (target >= 0 && target < ActionCount) _entries.Select(target);
    }

    /// <summary>
    /// Helper method to write what every action is bound to back onto its row.
    /// </summary>
    private void Refresh()
    {
        if (_gamepad is null) return;

        for (int i = 0; i < ActionCount; i++)
        {
            _entries[i].Label = ButtonGlyphs.Describe(_gamepad, GameInput.Actions[i].Name);
        }

        _hint.Text = ButtonGlyphs.Localize(HINT_BROWSE, _gamepad);
    }
}
