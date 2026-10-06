using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Fighter2D.Character;
using Horizon.Engine;
using Horizon.Input2;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where every player of this machine picks their character out of a grid, with whoever they are looking at shown big on the left
/// along with what there will be to know about them. The characters are whatever the content of the game has (Assets/data/characters.hor),
/// the cells there is no character for stay empty.
/// </summary>
internal class CharacterSelectScene(MatchSetup setup) : Scene
{
    private const int GRID_ROWS = 3;
    private const float INPUT_DELAY = 0.25f;

    // How long everybody gets to look at what they picked before we move on
    private const float LEAVE_DELAY = 0.45f;

    private const string HINT = "[icon:dpad] choose    [icon:pad_a] pick    [icon:pad_b] back";
    private const string PREVIEW_ANIMATION = "run_loop";

    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);
    private static readonly Vector4 ReadyColor = new(0.33f, 0.85f, 0.45f, 1.0f);
    private static readonly Vector2 CellSize = new(168, 178);

    /// <summary>
    /// One cell of the grid, a button with a character in it (or nothing).
    /// </summary>
    private sealed class Cell
    {
        public Button Box = null!;
        public Label Tags = null!;
        public CharacterPortrait? Portrait;
        public CharacterDefinition? Character;
    }

    public override Camera ActiveCamera { get; protected set; }

    // The glass everything is seen through
    private Renderer2D screen = null!;

    private List<CharacterDefinition> characters = [];
    private Cell[] cells = [];

    // How many cells there are next to each other, which is up to the layout
    private int gridColumns = 1;

    // Where every player is in the grid and whether they have made up their mind
    private int[] cursors = [];
    private bool[] confirmed = [];

    // The preview on the left, and the cell it is showing right now
    private CharacterPortrait preview;
    private Label previewName, hint;
    private ProgressBar speed, health, bulk, complexity;
    private int previewed = -1;
    private int wantsPreview = 0;

    private float _delayTimer = 0.0f;
    private float leaveTimer = -1.0f;

    public override void Initialize()
    {
        ActiveCamera = AddEntity(new Camera2D(Engine.WindowManager.WindowSize));
        Engine.GL.ClearColor(System.Drawing.Color.PaleVioletRed);

        characters = CharacterDefinition.LoadAll();

        // Everybody starts on the character they had last time, or the first one
        cursors = new int[setup.PlayerCount];
        confirmed = new bool[setup.PlayerCount];
        for (int i = 0; i < cursors.Length; i++)
        {
            cursors[i] = Math.Max(0, characters.FindIndex(character => character.Id == setup.GetCharacter(i)));
        }

        screen = Screen.For(this);

        CompositeImages();
        CompositeUi();

        base.Initialize();
    }

    public override void UpdateState(float dt)
    {
        _delayTimer += dt;
        base.UpdateState(dt);

        UpdateCells();

        // The portraits are images of the layout, their animations are ours to move along
        preview.Update(dt);
        foreach (Cell cell in cells)
        {
            cell.Portrait?.Update(dt);
        }

        if (leaveTimer >= 0)
        {
            leaveTimer -= dt;
            if (leaveTimer < 0) Continue();
            return;
        }

        // The button that got us here is most likely still held
        if (_delayTimer < INPUT_DELAY) return;

        for (int player = 0; player < cursors.Length; player++)
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
            if (!confirmed[player])
            {
                Back();
                return true;
            }

            confirmed[player] = false;
            return false;
        }

        // Somebody who has picked stays where they are
        if (confirmed[player]) return false;

        int column = (GameInput.MenuRightPressed(gamepad) ? 1 : 0) - (GameInput.MenuLeftPressed(gamepad) ? 1 : 0);
        int row = (GameInput.MenuDownPressed(gamepad) ? 1 : 0) - (GameInput.MenuUpPressed(gamepad) ? 1 : 0);

        if (column != 0 || row != 0)
        {
            Move(player, column, row);
        }
        else if (gamepad.WasPressed(GamepadInput.A))
        {
            Confirm(player);
        }

        return false;
    }

    private void Move(int player, int column, int row)
    {
        int x = Math.Clamp(cursors[player] % gridColumns + column, 0, gridColumns - 1);
        int y = Math.Clamp(cursors[player] / gridColumns + row, 0, GRID_ROWS - 1);
        int target = y * gridColumns + x;

        wantsPreview = player;

        // There is nobody in the empty cells to walk over to
        if (cells[target].Character is null)
        {
            cells[target].Box.Shake(4.0f, 0.2f);
            return;
        }

        cursors[player] = target;
    }

    private void Confirm(int player)
    {
        if (cells[cursors[player]].Character is not { } character) return;

        confirmed[player] = true;
        setup.SetCharacter(player, character.Id);
        cells[cursors[player]].Box.Punch(0.1f, 0.3f);

        if (Array.TrueForAll(confirmed, isConfirmed => isConfirmed))
        {
            leaveTimer = LEAVE_DELAY;
        }
    }

    /// <summary>
    /// Helper method to show who is on which cell, and whoever moved last on the left.
    /// </summary>
    private void UpdateCells()
    {
        var tags = new StringBuilder();

        for (int i = 0; i < cells.Length; i++)
        {
            tags.Clear();
            bool everybodyHere = true, anybodyHere = false;

            for (int player = 0; player < cursors.Length; player++)
            {
                if (cursors[player] != i) continue;

                if (tags.Length > 0) tags.Append(' ');
                tags.Append('P').Append(player + 1);

                anybodyHere = true;
                everybodyHere &= confirmed[player];
            }

            cells[i].Box.Selected = anybodyHere;
            cells[i].Tags.Text = tags.ToString();
            cells[i].Tags.Color = anybodyHere && everybodyHere ? ReadyColor : Vector4.One;
        }

        int looking = cursors[Math.Clamp(wantsPreview, 0, cursors.Length - 1)];
        if (looking != previewed) ShowPreview(looking);

        if (cursors.Length > 0 && setup.Slots.Count > 0 && GameInput.Manager.TryGet(setup.Slots[0], out Gamepad first))
        {
            hint.Text = GameInput.Localize(HINT, GameInput.Manager.LastUsed ?? first);
        }
    }

    private void ShowPreview(int cell)
    {
        if (cells[cell].Character is not { } character) return;

        previewed = cell;
        preview.Show(character, PREVIEW_ANIMATION);
        preview.Image.Punch(0.06f, 0.25f);

        previewName.Text = character.PrettyName;
        speed.Progress = character.Speed;
        health.Progress = character.Health;
        bulk.Progress = character.Bulk;
        complexity.Progress = character.Complexity;
    }

    /// <summary>
    /// Called once everybody has picked, on to whatever comes after.
    /// </summary>
    private void Continue()
    {
        // Online the lobby is where everything comes together, the map is chosen from there
        if (setup.IsOnline)
        {
            Engine.SetScene(new GamepadSelectorScene(setup));
            return;
        }

        Engine.SetScene(new MapSelectionScene(setup));
    }

    private void Back()
    {
        Engine.SetScene(new GamepadSelectorScene(setup));
    }

    private void CompositeImages()
    {
        var spriteBatch = screen.AddEntity<SpriteBatch>();

        if (Engine.ObjectManager.Textures.TryCreateOrGet("gpselbg", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/backgrounds/player_select_bg.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_bg))
        {
            var bg = spriteBatch.AddEntity(new Sprite(Engine.WindowManager.WindowSize));
            bg.Transform.SetPositionRelativeToOrigin(new Vector2(-Engine.WindowManager.WindowSize.X / 2, Engine.WindowManager.WindowSize.Y / 2));
            bg.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_bg.Asset, new Vector2(result_bg.Asset.Width, result_bg.Asset.Height)), "bg");

            spriteBatch.Add(bg);
        }
    }

    private void CompositeUi()
    {
        // The screen is laid out in Assets/ui/layouts/character_select.hor, a cell of the grid in character_cell.hor
        UILayout layout = MenuLayouts.Load(this, (Camera2D)ActiveCamera, MenuLayouts.CHARACTER_SELECT);

        // Whoever is being looked at on the left
        preview = new CharacterPortrait(layout.Get<Image>("preview"));
        previewName = layout.Get<Label>("preview_name");
        speed = layout.Get<ProgressBar>("speed");
        health = layout.Get<ProgressBar>("health");
        bulk = layout.Get<ProgressBar>("bulk");
        complexity = layout.Get<ProgressBar>("complexity");

        // Everybody there is to pick from on the right, as many next to each other as the layout says
        var grid = layout.Get<GridPanel>("grid");
        gridColumns = Math.Max(1, grid.Columns);

        var items = layout.Populate(grid, gridColumns * GRID_ROWS);
        cells = new Cell[items.Count];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = CompositeCell(items[i], i, i < characters.Count ? characters[i] : null);
        }

        // The skin draws the buttons of the gamepad where the text asks for them
        hint = layout.Get<Label>("hint");
    }

    /// <summary>
    /// Helper method to pick the parts of a cell out of the layout it was made from, and put its character in it.
    /// </summary>
    private Cell CompositeCell(UILayout item, int index, CharacterDefinition? character)
    {
        // A button so it lights up like one, clicking it is player one walking over and picking
        var box = item.Get<Button>("box");
        box.Enabled = character is not null;
        box.OnPressed = () =>
        {
            if (leaveTimer >= 0 || confirmed[0]) return;

            cursors[0] = index;
            wantsPreview = 0;
            Confirm(0);
        };

        var cell = new Cell
        {
            Box = box,
            Character = character,
            Tags = item.Get<Label>("tags")
        };

        var portrait = item.Get<Image>("portrait");
        var name = item.Get<Label>("name");

        // The cells there is nobody for show their question mark instead
        portrait.Visible = name.Visible = character is not null;
        item.Get<Label>("empty").Visible = character is null;

        if (character is null) return cell;

        cell.Portrait = new CharacterPortrait(portrait);
        cell.Portrait.Show(character);

        name.Text = character.PrettyName;
        return cell;
    }
}
