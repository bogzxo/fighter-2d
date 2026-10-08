using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where every player of this machine picks their character out of a grid, with whoever they are looking at shown big on the left.
/// The characters are whatever the content of the game has (Assets/data/characters.hor), the cells there is no character for stay empty.
/// </summary>
internal class CharacterSelectScene(MatchSetup setup) : MenuScene
{
    private const int GRID_ROWS = 3;

    // How long everybody gets to look at what they picked before we move on
    private const float LEAVE_DELAY = 0.45f;

    private const string HINT = "[icon:dpad] choose    [icon:pad_a] pick    [icon:pad_b] back";

    protected override string LayoutFile => MenuLayouts.CHARACTER_SELECT;

    private List<CharacterDefinition> _characters = [];
    private CharacterCell[] _cells = [];
    private CharacterPreview _preview = null!;
    private Label _hint = null!;

    // How many cells there are next to each other, which is up to the layout
    private int _columns = 1;

    // Where every player is in the grid and whether they have locked in
    private int[] _cursors = [];
    private bool[] _confirmed = [];

    // The player whose character the preview shows, which is whoever moved last
    private int _previewPlayer;

    // Counts down once everybody has picked, below zero while they haven't
    private float _leaveTimer = -1.0f;

    protected override void Prepare()
    {
        _characters = CharacterDefinition.LoadAll();

        // Everybody starts on the character they had last time, or the first one
        _cursors = new int[setup.PlayerCount];
        _confirmed = new bool[setup.PlayerCount];
        for (int i = 0; i < _cursors.Length; i++)
        {
            _cursors[i] = Math.Max(0, _characters.FindIndex(character => character.Id == setup.GetCharacter(i)));
        }
    }

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/character_select.hor, a cell of the grid in character_cell.hor
        _preview = new CharacterPreview(layout);
        _hint = layout.Get<Label>("hint");

        // As many cells next to each other as the layout says
        var grid = layout.Get<GridPanel>("grid");
        _columns = Math.Max(1, grid.Columns);

        var items = layout.Populate(grid, _columns * GRID_ROWS);
        _cells = new CharacterCell[items.Count];
        for (int i = 0; i < _cells.Length; i++)
        {
            int index = i;
            _cells[i] = new CharacterCell(items[i], i < _characters.Count ? _characters[i] : null);

            // Clicking a cell is player one walking over and picking it
            _cells[i].Box.OnPressed = () => ClickCell(index);
        }
    }

    protected override void UpdateMenu(float dt)
    {
        ShowCursors();

        // The portraits are images of the layout, their animations are ours to move along
        _preview.Update(dt);
        foreach (CharacterCell cell in _cells) cell.Update(dt);

        if (_leaveTimer >= 0)
        {
            _leaveTimer -= dt;
            if (_leaveTimer < 0) Continue();
            return;
        }

        if (!InputReady) return;

        for (int player = 0; player < _cursors.Length; player++)
        {
            if (player >= setup.Slots.Count || !GameInput.Manager.TryGet(setup.Slots[player], out Gamepad gamepad) || !gamepad.IsConnected)
            {
                // Somebody lost their gamepad, that is for the screen before this one to sort out
                Back();
                return;
            }

            if (UpdatePlayer(player, gamepad)) return;
        }
    }

    /// <summary>
    /// Called for every player with their gamepad, returns whether we have left the scene.
    /// </summary>
    private bool UpdatePlayer(int player, Gamepad gamepad)
    {
        if (gamepad.WasPressed(GamepadInput.B))
        {
            if (!_confirmed[player])
            {
                Back();
                return true;
            }

            // B on a character that is locked in only unlocks it
            _confirmed[player] = false;
            return false;
        }

        // Somebody who has picked stays where they are
        if (_confirmed[player]) return false;

        int column = MenuInput.Horizontal(gamepad), row = MenuInput.Vertical(gamepad);

        if (column != 0 || row != 0) Move(player, column, row);
        else if (gamepad.WasPressed(GamepadInput.A)) Confirm(player);

        return false;
    }

    private void Move(int player, int column, int row)
    {
        int x = Math.Clamp(_cursors[player] % _columns + column, 0, _columns - 1);
        int y = Math.Clamp(_cursors[player] / _columns + row, 0, GRID_ROWS - 1);
        int target = y * _columns + x;

        _previewPlayer = player;

        // There is nobody in the empty cells to walk over to
        if (_cells[target].Character is null)
        {
            _cells[target].Box.Shake(4.0f, 0.2f);
            return;
        }

        _cursors[player] = target;
    }

    private void Confirm(int player)
    {
        CharacterCell cell = _cells[_cursors[player]];
        if (cell.Character is not { } character) return;

        _confirmed[player] = true;
        setup.SetCharacter(player, character.Id);
        cell.Box.Punch(0.1f, 0.3f);

        if (Array.TrueForAll(_confirmed, confirmed => confirmed)) _leaveTimer = LEAVE_DELAY;
    }

    private void ClickCell(int index)
    {
        if (_leaveTimer >= 0 || _confirmed[0]) return;

        _cursors[0] = index;
        _previewPlayer = 0;
        Confirm(0);
    }

    /// <summary>
    /// Helper method to show who is on which cell, and whoever moved last in the preview.
    /// </summary>
    private void ShowCursors()
    {
        var tags = new StringBuilder();

        for (int i = 0; i < _cells.Length; i++)
        {
            tags.Clear();
            bool everybodyConfirmed = true;

            for (int player = 0; player < _cursors.Length; player++)
            {
                if (_cursors[player] != i) continue;

                if (tags.Length > 0) tags.Append(' ');
                tags.Append('P').Append(player + 1);

                everybodyConfirmed &= _confirmed[player];
            }

            // Green once everybody standing on the cell has locked it in
            _cells[i].ShowPlayers(tags.ToString(), tags.Length > 0 && everybodyConfirmed ? MenuColors.Ready : Vector4.One);
        }

        int looking = _cursors[Math.Clamp(_previewPlayer, 0, _cursors.Length - 1)];
        if (_cells[looking].Character is { } character) _preview.Show(character);

        if (setup.Slots.Count > 0 && GameInput.Manager.TryGet(setup.Slots[0], out Gamepad first))
        {
            _hint.Text = ButtonGlyphs.Localize(HINT, GameInput.Manager.LastUsed ?? first);
        }
    }

    /// <summary>
    /// Called once everybody has picked, on to whatever comes after.
    /// </summary>
    private void Continue()
    {
        // Online the lobby is where everything comes together, the map is chosen from there
        if (setup.IsOnline) GoTo(new GamepadSelectorScene(setup));
        else GoTo(new MapSelectionScene(setup));
    }

    private void Back() => GoTo(new GamepadSelectorScene(setup));
}
