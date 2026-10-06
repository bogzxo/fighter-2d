using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Fighter2D.Effects;
using Fighter2D.Networking;
using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Input2;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// The first scene of the game, where the kind of fight is picked (or the options, once there are any).
/// Behind the menu two fighters go at each other forever, see <see cref="MenuDuel"/>.
/// </summary>
internal class MainMenuScene : Scene
{
    private const float INPUT_DELAY = 0.25f;
    private const string HINT = "[icon:dpad] choose    [icon:pad_a] pick";

    // Where the fighters of the background meet and the ground they stand on, in the units of the camera (pixels of the window)
    private static readonly Vector2 DuelCenter = new(330, -330);

    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);

    public override Camera ActiveCamera { get; protected set; } = null!;
    private Camera2D camera = null!;

    // The glass everything is seen through
    private Renderer2D screen = null!;

    // Background
    private SpriteBatch spriteBatch = null!;
    private Sprite logo = null!;

    // UI Elements
    private readonly List<Button> buttons = [];
    private Label hint;

    // Whether the hint is busy saying something else than which buttons to press
    private bool hintIsMessage;
    private int selectedIndex = 0;
    private float _delayTimer = 0;

    public override void Initialize()
    {
        Engine.GL.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        Engine.GL.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);

        // Other scenes leave their own clear colour behind
        Engine.GL.ClearColor(System.Drawing.Color.Black);

        ActiveCamera = camera = AddEntity<Camera2D>(new(Engine.WindowManager.ViewportSize));

        screen = Screen.For(this);

        CompositeBackground();
        CompositeUi();

        Select(0);

        base.Initialize();
    }

    private void CompositeBackground()
    {
        // The backdrop goes in a batch of its own so it ends up behind everything else
        var backdropBatch = screen.AddEntity<SpriteBatch>();

        if (Engine.ObjectManager.Textures.TryCreateOrGet("main_menu_bg", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/backgrounds/background_layer_albedo.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_bg))
        {
            // Scaled up until it covers the window whatever its shape, whatever sticks out is simply off screen
            Vector2 window = Engine.WindowManager.WindowSize;
            float scale = MathF.Max(window.X / result_bg.Asset.Width, window.Y / result_bg.Asset.Height);

            var bg = backdropBatch.AddEntity(new Sprite(new Vector2(result_bg.Asset.Width, result_bg.Asset.Height) * scale));
            bg.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_bg.Asset, new Vector2(result_bg.Asset.Width, result_bg.Asset.Height)), "bg");

            backdropBatch.Add(bg);
        }

        // The right of the screen belongs to the duel, which looks after itself
        var duel = screen.AddEntity(new MenuDuel(DuelCenter));

        spriteBatch = screen.AddEntity<SpriteBatch>();

        if (Engine.ObjectManager.Textures.TryCreateOrGet("main_logo", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/ui/new_logo.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_logo))
        {
            const uint logoScalar = 2;

            logo = spriteBatch.AddEntity(new Sprite(new System.Numerics.Vector2(result_logo.Asset.Width * logoScalar, result_logo.Asset.Height * logoScalar)));
            logo.Transform.Origin = Origin.TopLeft;
            logo.Transform.Position = new Vector2(Engine.WindowManager.WindowSize.X / -2, Engine.WindowManager.WindowSize.Y / 2);
            logo.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_logo.Asset, new System.Numerics.Vector2(result_logo.Asset.Width, result_logo.Asset.Height)), "logo");

            spriteBatch.Add(logo);

            // The logo grows out of its corner, and takes every hit of the duel along with whoever blocked it
            logo.PopIn(0.7f, 0.15f);
            duel.Struck += () => logo.Shake(8.0f, 0.35f);
        }
    }

    private void CompositeUi()
    {
        // The menu is laid out in Assets/ui/layouts/main_menu.hor (how it slides in and its buttons pop up as well),
        // what its buttons do is decided here
        UILayout layout = MenuLayouts.Load(this, camera, MenuLayouts.MAIN_MENU);

        AddButton(layout, "btn_pvp", () => Play(MatchMode.Pvp));
        AddButton(layout, "btn_practice", () => Play(MatchMode.Practice));
        AddButton(layout, "btn_host", HostServer);
        AddButton(layout, "btn_join", JoinMultiplayer);
        AddButton(layout, "btn_options", OpenOptions);

        // The skin draws the buttons of the gamepad where the text asks for them
        hint = layout.Get<Label>("hint");
        layout.Get<Label>("version").Text = Constants.VERSION_LABEL;
    }

    private void AddButton(UILayout layout, string name, Action pressed)
    {
        var button = layout.Get<Button>(name);
        button.OnPressed = pressed;

        buttons.Add(button);
    }

    private void Select(int index)
    {
        selectedIndex = Math.Clamp(index, 0, buttons.Count - 1);

        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].Selected = i == selectedIndex;
        }
    }

    private void Play(MatchMode mode)
    {
        Engine.SetScene(new GamepadSelectorScene(new MatchSetup(mode)));
    }

    private void HostServer()
    {
        // The server is up before the lobby is, whoever joins waits there with us
        var session = NetSession.Host();
        if (session.HasFailed)
        {
            session.Dispose();

            hintIsMessage = true;
            hint.Text = $"port {NetSession.PORT} is taken";
            hint.Shake(6.0f);
            return;
        }

        Engine.SetScene(new GamepadSelectorScene(MatchSetup.Online(session)));
    }

    private void JoinMultiplayer()
    {
        // Which server is asked there, the lobby comes after
        Engine.SetScene(new JoinServerScene());
    }

    private void OpenOptions()
    {
        // @bogz the options screen goes here, Engine.SetScene(new OptionsScene()) once there is one
        hintIsMessage = true;
        hint.Text = "options are on their way";
        hint.Shake(6.0f);
    }

    public override void UpdateState(float dt)
    {
        _delayTimer += dt;

        base.UpdateState(dt);

        // The hint shows the buttons of whichever gamepad was touched last
        if (!hintIsMessage)
        {
            hint.Text = GameInput.Localize(HINT, GameInput.Manager.LastUsed);
        }

        // The button that got us here is most likely still held
        if (_delayTimer < INPUT_DELAY) return;

        // Nobody has picked a gamepad yet, so the menu listens to all of them
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            if (GameInput.MenuDownPressed(gamepad))
            {
                Select(selectedIndex + 1);
            }
            else if (GameInput.MenuUpPressed(gamepad))
            {
                Select(selectedIndex - 1);
            }
            else if (gamepad.WasPressed(GamepadInput.A) || gamepad.WasPressed(GamepadInput.Start))
            {
                buttons[selectedIndex].OnPressed?.Invoke();
                return;
            }
        }
    }
}
