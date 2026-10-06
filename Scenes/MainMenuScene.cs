using System;
using System.Numerics;

using Fighter2D.Effects;
using Fighter2D.Match;
using Fighter2D.Networking;

using Horizon.Rendering;
using Horizon.Input2;
using Horizon.OpenGL.Assets;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Silk.NET.OpenGL;

using Button = Horizon.Rendering.UIX.Components.Button;
using Texture = Horizon.OpenGL.Assets.Texture;

namespace Fighter2D.Scenes;

/// <summary>
/// The first scene of the game, where the kind of fight is picked (or the options, once there are any).
/// Behind the menu two fighters go at each other forever, see <see cref="MenuDuel"/>.
/// </summary>
internal class MainMenuScene : MenuScene
{
    private const string HINT = "[icon:dpad] choose    [icon:pad_a] pick";
    private const uint LOGO_SCALE = 2;

    // Where the fighters of the background meet and the ground they stand on, in pixels of the window
    private static readonly Vector2 DuelCenter = new(330, -330);

    protected override string LayoutFile => MenuLayouts.MAIN_MENU;
    protected override Vector2 CameraSize => Engine.WindowManager.ViewportSize;
    protected override System.Drawing.Color ClearColor => System.Drawing.Color.Black;

    private readonly ButtonList _buttons = new();
    private Label _hint = null!;

    // Whether the hint is busy saying something else than which buttons to press
    private bool _hintIsMessage;

    protected override void BuildBackdrop()
    {
        Engine.GL.Enable(EnableCap.Blend);
        Engine.GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // The picture goes in a batch of its own so it ends up behind everything else
        var backdropBatch = Canvas.AddEntity<SpriteBatch>();

        if (TryLoadTexture("main_menu_bg", "Assets/backgrounds/background_layer_albedo.png", out Texture background))
        {
            // Scaled up until it covers the window whatever its shape, whatever sticks out is simply off screen
            Vector2 window = Engine.WindowManager.WindowSize;
            Vector2 size = new(background.Width, background.Height);
            float scale = MathF.Max(window.X / size.X, window.Y / size.Y);

            var sprite = backdropBatch.AddEntity(new Sprite(size * scale));
            sprite.ConfigureSpriteSheet(SpriteSheet.FromTexture(background, size), "bg");
            backdropBatch.Add(sprite);
        }

        // The right of the screen belongs to the duel, which looks after itself
        var duel = Canvas.AddEntity(new MenuDuel(DuelCenter));

        AddLogo(duel);
    }

    private void AddLogo(MenuDuel duel)
    {
        if (!TryLoadTexture("main_logo", "Assets/ui/new_logo.png", out Texture texture)) return;

        var batch = Canvas.AddEntity<SpriteBatch>();
        Vector2 size = new(texture.Width, texture.Height);

        var logo = batch.AddEntity(new Sprite(size * LOGO_SCALE));
        logo.Transform.Origin = Origin.TopLeft;
        logo.Transform.Position = new Vector2(Engine.WindowManager.WindowSize.X / -2, Engine.WindowManager.WindowSize.Y / 2);
        logo.ConfigureSpriteSheet(SpriteSheet.FromTexture(texture, size), "logo");
        batch.Add(logo);

        // The logo grows out of its corner, and rattles along with every kick of the duel
        logo.PopIn(0.7f, 0.15f);
        duel.Struck += () => logo.Shake(8.0f, 0.35f);
    }

    protected override void BuildUi(UILayout layout)
    {
        // The menu is laid out in Assets/ui/layouts/main_menu.hor, what its buttons do is decided here
        AddButton(layout, "btn_pvp", () => Play(MatchMode.Pvp));
        AddButton(layout, "btn_practice", () => Play(MatchMode.Practice));
        AddButton(layout, "btn_host", HostServer);
        AddButton(layout, "btn_join", () => GoTo(new JoinServerScene()));
        AddButton(layout, "btn_options", OpenOptions);

        _hint = layout.Get<Label>("hint");
        layout.Get<Label>("version").Text = Constants.VERSION_LABEL;

        _buttons.Select(0);
    }

    private void AddButton(UILayout layout, string name, Action pressed)
    {
        var button = layout.Get<Button>(name);
        button.OnPressed = pressed;

        _buttons.Add(button);
    }

    private void Play(MatchMode mode) => GoTo(new GamepadSelectorScene(new MatchSetup(mode)));

    private void HostServer()
    {
        // The server is up before the lobby is, whoever joins waits there with us
        var session = NetSession.Host();
        if (session.HasFailed)
        {
            session.Dispose();
            ShowMessage($"port {NetSession.PORT} is taken");
            return;
        }

        GoTo(new GamepadSelectorScene(MatchSetup.Online(session)));
    }

    private void OpenOptions()
    {
        // @bogz the options screen goes here, GoTo(new OptionsScene()) once there is one
        ShowMessage("options are on their way");
    }

    private void ShowMessage(string message)
    {
        _hintIsMessage = true;
        _hint.Text = message;
        _hint.Shake(6.0f);
    }

    protected override void UpdateMenu(float dt)
    {
        // The hint shows the buttons of whichever gamepad was touched last
        if (!_hintIsMessage) _hint.Text = ButtonGlyphs.Localize(HINT, GameInput.Manager.LastUsed);

        if (!InputReady) return;

        // Nobody has picked a gamepad yet, so the menu listens to all of them
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            _buttons.Move(MenuInput.Vertical(gamepad));

            if (MenuInput.ConfirmPressed(gamepad))
            {
                _buttons.Press();
                return;
            }
        }
    }
}
