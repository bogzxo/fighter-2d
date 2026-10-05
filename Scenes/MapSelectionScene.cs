using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Text;

using Fighter2D.Character;
using Fighter2D.Character.Controllers;
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
/// Joining a server happens here too, on a second page where its address is typed in with an on-screen keyboard.
/// </summary>
internal class MapSelectionScene(MatchSetup setup) : Scene
{
    // Player one drives the menus
    private int gamepadIndex => setup.Slots.Count > 0 ? setup.Slots[0] : 0;

    private const int DescriptionLineLength = 34;

    // Hints are dimmed by their colour rather than by being see-through, that would fade the icons in them as well
    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);
    private const string KEY_BACK = "BACK";
    private const string KEY_CONNECT = "CONNECT";
    private const string HINT_MAP = "[icon:dpad] choose a map    [icon:pad_a] fight    [icon:pad_y] join a server    [icon:pad_b] back";
    private const string HINT_JOIN = "[icon:dpad] move    [icon:pad_a] press    [icon:pad_b] delete    [icon:pad_y] connect";

    [Flags]
    private enum PadButtons
    {
        None = 0,
        Up = 1 << 0,
        Down = 1 << 1,
        Left = 1 << 2,
        Right = 1 << 3,
        A = 1 << 4,
        B = 1 << 5,
        Y = 1 << 6
    }

    public override Camera ActiveCamera { get; protected set; }
    private Camera2D camera;

    private MapLoader.MapDefinition[] mapDefinitions;
    private readonly List<Button> mapButtons = [];
    private Label title, description, addressError, mapHint, joinHint;
    private StackPanel mapPage, joinPage;
    private TextBox addressBox;
    private OnScreenKeyboard keyboard;
    private int selectedIndex = 0;
    private Gamepad? _gamepad;
    private PadButtons heldButtons;
    private float totalEngineTime = 0;

    public override void Initialize()
    {
        mapDefinitions = [.. MapLoader.LoadDefinitions("Assets/data/maps.hor")];

        ActiveCamera = camera = AddEntity<Camera2D>(new(Engine.WindowManager.WindowSize));
        Engine.GL.ClearColor(System.Drawing.Color.PaleVioletRed);

        CompositeImages();
        CompositeUi();

        // This forces the first map to be shown as selected
        SelectMap(0);

        base.Initialize();
    }

    private void CompositeImages()
    {
        var spriteBatch = AddEntity<SpriteBatch>();

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
        var compositor = AddComponent(new UICompositor(camera, Constants.UI_THEME));
        var panel = compositor.CreateModule().AddComponent(new StackPanel
        {
            Background = "panel",
            Padding = new UIEdges(40, 34),
            Spacing = 24
        });

        title = panel.Add(new Label("Select Map") { TextScale = 0.6f });

        // Only one of the two pages is ever shown
        mapPage = panel.Add(new StackPanel { Spacing = 24 });
        joinPage = panel.Add(new StackPanel { Spacing = 16, Visible = false });

        CompositeMapPage();
        CompositeJoinPage();

        panel.PopIn(0.4f);
    }

    private void CompositeMapPage()
    {
        // The maps on the left, what there is to say about the selected one on the right
        var columns = mapPage.Add(new StackPanel { Direction = UIDirection.Horizontal, Spacing = 28 });
        var maps = columns.Add(new StackPanel { Stretch = true, Spacing = 10 });

        for (int i = 0; i < mapDefinitions.Length; i++)
        {
            // Clicking a map selects it, the buttons underneath (or the gamepad) start the fight
            // A height of zero leaves the buttons as tall as the skin draws them
            int index = i;
            mapButtons.Add(maps.Add(new Button(mapDefinitions[i].PrettyName)
            {
                Size = new Vector2(280, 0),
                OnPressed = () => SelectMap(index)
            }));
        }

        description = columns.Add(new Label
        {
            Anchor = Origin.Top,
            Align = Origin.TopLeft,
            Size = new Vector2(440, 130),
            TextScale = 0.3f
        });

        var actions = mapPage.Add(new StackPanel { Direction = UIDirection.Horizontal, Spacing = 16 });
        actions.Add(new Button("Fight") { Size = new Vector2(200, 0), OnPressed = StartFight });
        actions.Add(new Button("Join Server") { Size = new Vector2(280, 0), OnPressed = () => ShowJoinPage(true) });

        // The skin draws the buttons of the gamepad where the text asks for them
        mapHint = mapPage.Add(new Label(HINT_MAP)
        {
            TextScale = 0.25f,
            Color = HintColor
        });
    }

    private void CompositeJoinPage()
    {
        joinPage.Add(new Label("Address of the server") { TextScale = 0.3f });

        // A real keyboard can type into the box as well, enter connects
        addressBox = joinPage.Add(new TextBox("127.0.0.1")
        {
            Size = new Vector2(420, 56),
            MaxLength = 15,
            AllowedCharacters = "0123456789.",
            OnChanged = _ => addressError.Text = string.Empty,
            OnSubmitted = _ => Connect()
        });

        // Empty until an address turns out to be wrong, kept at a fixed height so the keys don't jump around when it shows
        addressError = joinPage.Add(new Label
        {
            Size = new Vector2(0, 22),
            TextScale = 0.25f,
            Color = new Vector4(1.0f, 0.45f, 0.4f, 1.0f)
        });

        // The digits type into the box by themselves, the two keys on the last row are ours to handle
        keyboard = joinPage.Add(new OnScreenKeyboard([.. OnScreenKeyboard.Numeric, [KEY_BACK, KEY_CONNECT]])
        {
            Target = addressBox,
            OnKey = OnJoinKey
        });

        joinHint = joinPage.Add(new Label(HINT_JOIN)
        {
            TextScale = 0.25f,
            Color = HintColor
        });
    }

    public override void UpdateState(float dt)
    {
        totalEngineTime += dt;

        // Attach the gamepad that was picked on the screen before this one
        if (_gamepad is null)
        {
            GameInput.Manager.TryGet(gamepadIndex, out _gamepad);
        }

        // The hints show the buttons the way they are printed on that gamepad
        mapHint.Text = GameInput.Localize(HINT_MAP, _gamepad);
        joinHint.Text = GameInput.Localize(HINT_JOIN, _gamepad);

        // Only act on the press itself, otherwise holding a button repeats it on every update
        PadButtons held = ReadGamepad();
        PadButtons pressed = held & ~heldButtons;
        heldButtons = held;

        if (joinPage.Visible)
        {
            UpdateJoinPage(pressed);
        }
        else
        {
            // Process Scene Transition on the MAIN THREAD safely
            if (totalEngineTime > 1.0f && Engine.InputManager.IsPressed(Horizon.Input.VirtualAction.Interact))
            {
                StartFight();
                return;
            }

            UpdateMapPage(pressed);
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
            (GameInput.MenuLeft(_gamepad) ? PadButtons.Left : PadButtons.None) |
            (GameInput.MenuRight(_gamepad) ? PadButtons.Right : PadButtons.None) |
            (_gamepad.IsDown(GamepadInput.A) ? PadButtons.A : PadButtons.None) |
            (_gamepad.IsDown(GamepadInput.B) ? PadButtons.B : PadButtons.None) |
            (_gamepad.IsDown(GamepadInput.Y) ? PadButtons.Y : PadButtons.None)
        );
    }

    private void UpdateMapPage(PadButtons pressed)
    {
        if (pressed.HasFlag(PadButtons.Y))
        {
            ShowJoinPage(true);
        }
        else if (pressed.HasFlag(PadButtons.B))
        {
            // Back to picking a gamepad, which is also where its bindings are changed
            Engine.SetScene(new GamepadSelectorScene(setup));
        }
        else if (pressed.HasFlag(PadButtons.Down))
        {
            SelectMap(selectedIndex + 1);
        }
        else if (pressed.HasFlag(PadButtons.Up))
        {
            SelectMap(selectedIndex - 1);
        }
    }

    private void UpdateJoinPage(PadButtons pressed)
    {
        // Move the highlighted key around with the d-pad
        int right = (pressed.HasFlag(PadButtons.Right) ? 1 : 0) - (pressed.HasFlag(PadButtons.Left) ? 1 : 0);
        int down = (pressed.HasFlag(PadButtons.Down) ? 1 : 0) - (pressed.HasFlag(PadButtons.Up) ? 1 : 0);
        if (right != 0 || down != 0)
        {
            keyboard.Navigate(right, down);
        }

        if (pressed.HasFlag(PadButtons.A))
        {
            keyboard.Press();
        }
        else if (pressed.HasFlag(PadButtons.B))
        {
            addressBox.Backspace();
        }
        else if (pressed.HasFlag(PadButtons.Y))
        {
            Connect();
        }
    }

    /// <summary>
    /// Called for every key of the on-screen keyboard, the ones that type have already done so by now.
    /// </summary>
    private void OnJoinKey(string key)
    {
        if (key == KEY_BACK)
        {
            ShowJoinPage(false);
        }
        else if (key == KEY_CONNECT)
        {
            Connect();
        }
    }

    private void ShowJoinPage(bool show)
    {
        mapPage.Visible = !show;
        joinPage.Visible = show;

        // The page that takes over slides in from the side it lives on
        (show ? joinPage : mapPage).SlideIn(new Vector2(show ? 220 : -220, 0), 0.3f);
        title.Text = show ? "Join Server" : "Select Map";
        addressError.Text = string.Empty;

        if (show)
        {
            // The box takes whatever is typed on a real keyboard for as long as the page is up
            addressBox.Focus();
            keyboard.Select("1");
        }
        else
        {
            addressBox.Unfocus();

            // The button that got us back here is most likely still held (A), dont let it start the fight
            totalEngineTime = 0;
        }
    }

    private void StartFight()
    {
        var mapDefinition = mapDefinitions[selectedIndex];

        if (setup.Mode == MatchMode.Pvp && setup.Slots.Count > 1)
        {
            // Player two is just another local player on the second gamepad
            Engine.SetScene(new FightScene(mapDefinition, gamepadIndex, new Player()
            {
                Controller = new LocalPlayerController(new GamepadPlayerInput(setup.Slots[1])),
                SpawnPosition = new(mapDefinition.SpawnPosition.X * 16 + 256, TileMapChunk.HEIGHT * 16 - mapDefinition.SpawnPosition.Y * 16),
            }));
            return;
        }

        Engine.SetScene(new FightScene(mapDefinition, gamepadIndex));
    }

    private void Connect()
    {
        var mapDefinition = mapDefinitions[selectedIndex];
        string text = addressBox.Text.Trim();

        if (!IPAddress.TryParse(text, out _))
        {
            addressError.Text = "dude cmon.";
            addressBox.Shake(10.0f);
            return;
        }

        Engine.SetScene(new FightScene(mapDefinition, gamepadIndex, new NetworkPlayer(text)
        {
            SpawnPosition = new(mapDefinition.SpawnPosition.X * 16 + 256, TileMapChunk.HEIGHT * 16 - mapDefinition.SpawnPosition.Y * 16),
        }));
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
