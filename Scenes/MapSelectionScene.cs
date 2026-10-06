using System;
using System.Collections.Generic;
using System.Text;

using Fighter2D.Map;
using Fighter2D.Match;

using Horizon.Input2;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the map of the fight is picked, either with the gamepad or by clicking.
/// In an online fight this is the host's job, the other player waits in the lobby and follows them into the fight.
/// </summary>
internal class MapSelectionScene(MatchSetup setup) : MenuScene
{
    private const int DESCRIPTION_LINE_LENGTH = 34;
    private const string HINT = "[icon:dpad] choose a map    [icon:pad_a] fight    [icon:pad_b] back";

    // How long (in seconds) the keyboard has to wait before it can start the fight, it has no press to tell apart from a hold
    private const float KEYBOARD_DELAY = 1.0f;

    protected override string LayoutFile => MenuLayouts.MAP_SELECT;
    protected override float InputDelay => 0.3f;

    private List<MapDefinition> _maps = [];
    private readonly ButtonList _buttons = new();
    private Label _description = null!, _hint = null!;

    protected override void Prepare()
    {
        _maps = MapLoader.LoadAll();
    }

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/map_select.hor, the button of a map in map_button.hor
        var items = layout.Populate("maps", _maps.Count);

        for (int i = 0; i < items.Count; i++)
        {
            // Clicking a map selects it, the fight button underneath (or the gamepad) starts the fight
            int index = i;
            var button = items[i].Get<Button>("button");
            button.Label = _maps[i].PrettyName;
            button.OnPressed = () => SelectMap(index);

            _buttons.Add(button);
        }

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

        if (SceneTime > KEYBOARD_DELAY && Engine.InputManager.IsPressed(Horizon.Input.VirtualAction.Interact))
        {
            Continue();
            return;
        }

        if (gamepad is null || !InputReady) return;

        int move = MenuInput.Vertical(gamepad);

        if (gamepad.WasPressed(GamepadInput.A)) Continue();
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
        _description.Text = WrapText(_maps[_buttons.Selected].Description, DESCRIPTION_LINE_LENGTH);
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
