using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Horizon.Engine;
using Horizon.Input;
using Horizon.Physics;
using Horizon.UI;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the map of the fight is picked, either with the gamepad or by clicking.
/// Behind the menu is a live preview of whichever map is selected, see <see cref="MapPreview"/>.
/// In an online fight this is the host's job, the other player waits in the lobby and follows them into the fight.
/// </summary>
internal class MapSelectionScene(MatchSetup setup) : MenuScene
{
    private const int DESCRIPTION_LINE_LENGTH = 26;
    private const string HINT = "[icon:dpad] choose    [icon:pad_a] fight    [icon:pad_b] back";

    // The menu covers the left of the screen, so the preview looks a bit to the left of where the fight starts to put that spot in what is left
    private static readonly Vector2 PREVIEW_OFFSET = new(-110, 0);

    protected override string LayoutFile => MenuLayouts.MAP_SELECT;
    protected override float InputDelay => 0.3f;

    private List<MapDefinition> _maps = [];
    private readonly ButtonList _buttons = new();
    private Label _description = null!, _hint = null!;
    private MapPreview _preview = null!;

    protected override void Prepare()
    {
        _maps = MapLoader.LoadAll();
    }

    protected override void BuildBackdrop()
    {
        Vector2 viewport = Engine.WindowManager.ViewportSize;

        // The preview is a piece of the world, so the scene looks at it through a world camera the way a fight does.
        // The menu keeps the camera it was given, a UI doesn't care what the scene is looking at
        var worldCamera = AddEntity(new Camera2D(viewport / 2.0f));
        ActiveCamera = worldCamera;

        // Only there for the weather to live in, nothing ever collides with anything on this screen
        var world = AddComponent<PhysicsWorld>();

        _preview = Canvas.AddEntity(new MapPreview(worldCamera, world, viewport, PREVIEW_OFFSET));
    }

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/map_select.hor, the button of a map in map_button.hor
        // A button per map. Clicking one selects it, the fight button underneath (or the gamepad) starts the fight
        layout.Populate("maps", _maps, (item, map, index) =>
        {
            var button = item.Get<Button>("button");
            button.Label = map.PrettyName;
            button.OnPressed = () => SelectMap(index);

            _buttons.Add(button);
        });

        _description = layout.Get<Label>("description");
        _hint = layout.Get<Label>("hint");
        layout.Get<Button>("btn_fight").OnPressed = Continue;

        SelectMap(0);
    }

    protected override void UpdateMenu(float dt)
    {
        // The other player left while we were choosing, back to the lobby to wait for the next one
        if (setup.Lobby is { PeerPresent: false })
        {
            Back();
            return;
        }

        // Player one drives the menus, and the hint shows the buttons the way they are printed on their gamepad
        GameInput.Manager.TryGet(setup.MenuSlot, out Gamepad? gamepad);
        _hint.Text = ButtonGlyphs.Localize(HINT, gamepad);

        if (gamepad is null || !InputReady) return;

        int move = MenuInput.Vertical(gamepad);

        if (MenuInput.ConfirmPressed(gamepad)) Continue();
        else if (gamepad.WasPressed(GamepadInput.B)) Back();
        else if (move != 0) SelectMap(_buttons.Selected + move);
    }

    /// <summary>
    /// Helper method to go back to picking a gamepad, which is also the lobby of an online fight.
    /// </summary>
    private void Back()
    {
        setup.Lobby?.SetChoosingMap(false);
        GoTo(new GamepadSelectorScene(setup));
    }

    /// <summary>
    /// The rules of the match come next, the fight after that.
    /// </summary>
    private void Continue()
    {
        if (_maps.Count > 0) GoTo(new MatchRulesScene(setup, _maps[_buttons.Selected]));
    }

    private void SelectMap(int index)
    {
        if (_maps.Count == 0) return;

        _buttons.Select(index);

        MapDefinition map = _maps[_buttons.Selected];
        _description.Text = WrapText(map.Description, DESCRIPTION_LINE_LENGTH);
        _preview.Show(map);
    }

    /// <summary>
    /// Helper method to break a text into lines of a maximum length, labels only start a new line where they are told to.
    /// </summary>
    private static string WrapText(string text, int lineLength)
    {
        var wrapped = new StringBuilder();
        int length = 0;

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (length > 0 && length + 1 + word.Length > lineLength)
            {
                wrapped.Append('\n');
                length = 0;
            }
            else if (length > 0)
            {
                wrapped.Append(' ');
                length++;
            }

            wrapped.Append(word);
            length += word.Length;
        }

        return wrapped.ToString();
    }
}
