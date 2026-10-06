using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Horizon.Engine;
using Horizon.Input2;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the map of the fight is picked, either with the gamepad or by clicking.
/// In an online fight this is the host's to do, the other player waits in the lobby and follows them into the fight.
/// </summary>
internal class MapSelectionScene(MatchSetup setup) : Scene
{
    // Player one drives the menus
    private int gamepadIndex => setup.Slots.Count > 0 ? setup.Slots[0] : 0;

    private const int DescriptionLineLength = 34;

    // Hints are dimmed by their colour rather than by being see-through, that would fade the icons in them as well
    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);
    private const string HINT_MAP = "[icon:dpad] choose a map    [icon:pad_a] fight    [icon:pad_b] back";

    [Flags]
    private enum PadButtons
    {
        None = 0,
        Up = 1 << 0,
        Down = 1 << 1,
        A = 1 << 2,
        B = 1 << 3
    }

    public override Camera ActiveCamera { get; protected set; }

    // The glass everything is seen through
    private Renderer2D screen = null!;
    private Camera2D camera;

    private MapLoader.MapDefinition[] mapDefinitions;
    private readonly List<Button> mapButtons = [];
    private Label description, mapHint;
    private int selectedIndex = 0;
    private Gamepad? _gamepad;
    private PadButtons heldButtons;
    private float totalEngineTime = 0;

    public override void Initialize()
    {
        mapDefinitions = [.. MapLoader.LoadDefinitions(Fighter2D.Content.GameContent.PathOf(Fighter2D.Content.GameContent.MAPS_FILE))];

        ActiveCamera = camera = AddEntity<Camera2D>(new(Engine.WindowManager.WindowSize));
        Engine.GL.ClearColor(System.Drawing.Color.PaleVioletRed);

        screen = Screen.For(this);

        CompositeImages();
        CompositeUi();

        // This forces the first map to be shown as selected
        SelectMap(0);

        base.Initialize();
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
        // The screen is laid out in Assets/ui/layouts/map_select.hor, the button of a map in map_button.hor
        UILayout layout = MenuLayouts.Load(this, camera, MenuLayouts.MAP_SELECT);

        // The maps on the left, what there is to say about the selected one on the right
        var items = layout.Populate("maps", mapDefinitions.Length);

        for (int i = 0; i < items.Count; i++)
        {
            // Clicking a map selects it, the button underneath (or the gamepad) starts the fight
            int index = i;
            var button = items[i].Get<Button>("button");
            button.Label = mapDefinitions[i].PrettyName;
            button.OnPressed = () => SelectMap(index);

            mapButtons.Add(button);
        }

        description = layout.Get<Label>("description");
        layout.Get<Button>("btn_fight").OnPressed = StartFight;

        // The skin draws the buttons of the gamepad where the text asks for them
        mapHint = layout.Get<Label>("hint");
    }

    public override void UpdateState(float dt)
    {
        totalEngineTime += dt;

        // The other player left while we were choosing, back to the lobby to wait for the next one
        if (setup.Lobby is { PeerPresent: false })
        {
            Back();
            return;
        }

        // Attach the gamepad that was picked on the screen before this one
        if (_gamepad is null)
        {
            GameInput.Manager.TryGet(gamepadIndex, out _gamepad);
        }

        // The hint shows the buttons the way they are printed on that gamepad
        mapHint.Text = GameInput.Localize(HINT_MAP, _gamepad);

        // Only act on the press itself, otherwise holding a button repeats it on every update
        PadButtons held = ReadGamepad();
        PadButtons pressed = held & ~heldButtons;
        heldButtons = held;

        // Process Scene Transition on the MAIN THREAD safely
        if (totalEngineTime > 1.0f && Engine.InputManager.IsPressed(Horizon.Input.VirtualAction.Interact))
        {
            StartFight();
            return;
        }

        // The button that got us here is most likely still held, it has to be pressed again to count
        if (pressed.HasFlag(PadButtons.A) && totalEngineTime > 0.3f)
        {
            StartFight();
            return;
        }

        if (pressed.HasFlag(PadButtons.B))
        {
            Back();
            return;
        }
        else if (pressed.HasFlag(PadButtons.Down))
        {
            SelectMap(selectedIndex + 1);
        }
        else if (pressed.HasFlag(PadButtons.Up))
        {
            SelectMap(selectedIndex - 1);
        }

        base.UpdateState(dt);
    }

    private PadButtons ReadGamepad()
    {
        if (_gamepad is null) return PadButtons.None;

        // Menus stay on the buttons they are drawn with, whatever the gamepad is bound to in a fight
        return (
            (GameInput.MenuUp(_gamepad) ? PadButtons.Up : PadButtons.None) |
            (GameInput.MenuDown(_gamepad) ? PadButtons.Down : PadButtons.None) |
            (_gamepad.IsDown(GamepadInput.A) ? PadButtons.A : PadButtons.None) |
            (_gamepad.IsDown(GamepadInput.B) ? PadButtons.B : PadButtons.None)
        );
    }

    /// <summary>
    /// Helper method to go back to picking a gamepad, which is also where its bindings are changed (and the lobby of an online fight)
    /// </summary>
    private void Back()
    {
        setup.Lobby?.SetChoosingMap(false);
        Engine.SetScene(new GamepadSelectorScene(setup));
    }

    private void StartFight()
    {
        var mapDefinition = mapDefinitions[selectedIndex];

        // What the match is played by comes next, the fight after that. In an online fight the other player is still waiting in the lobby
        Engine.SetScene(new MatchRulesScene(setup, mapDefinition));
    }

    private void SelectMap(int index)
    {
        selectedIndex = Math.Clamp(index, 0, mapDefinitions.Length - 1);

        for (int i = 0; i < mapButtons.Count; i++)
        {
            mapButtons[i].Selected = i == selectedIndex;
        }

        description.Text = WrapText(mapDefinitions[selectedIndex].Description, DescriptionLineLength);
    }

    /// <summary>
    /// Helper method to break a text into lines of a maximum length, as labels only start a new line where they are told to.
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
