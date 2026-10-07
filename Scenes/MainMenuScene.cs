using System;
using System.Numerics;

using Horizon.Rendering;
using Horizon.Input;
using Horizon.OpenGL.Assets;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Silk.NET.OpenGL;

using Button = Horizon.Rendering.UIX.Components.Button;
using Texture = Horizon.OpenGL.Assets.Texture;

namespace Fighter2D.Scenes;

/// <summary>
/// The first scene of the game, where the kind of fight is picked (or the options).
/// Behind the menu two fighters go at each other forever, see <see cref="MenuDuel"/>.
/// </summary>
internal class MainMenuScene : MenuScene
{
    private const string HINT = "[icon:dpad] choose    [icon:pad_a] pick";

    // Where the fighters of the background meet and the ground they stand on, in pixels of the window
    private static readonly Vector2 DuelCenter = new(330, -330);

    protected override string LayoutFile => MenuLayouts.MAIN_MENU;
    protected override Vector2 CameraSize => Engine.WindowManager.ViewportSize;
    protected override System.Drawing.Color ClearColor => System.Drawing.Color.Black;

    private readonly ButtonList _buttons = new();
    private MenuDuel? _duel;
    private Label _hint = null!;

    // Whether the hint is busy saying something else than which buttons to press
    private bool _hintIsMessage;

    protected override void BuildBackdrop()
    {
        // The picture goes in a batch of its own so it ends up behind everything else
        var backdropBatch = Canvas.AddEntity<SpriteBatch>();

        if (TryLoadTexture("Assets/backgrounds/background_layer_albedo.png", out Texture background))
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
        _duel = Canvas.AddEntity(new MenuDuel(DuelCenter));
    }

    protected override void BuildUi(UILayout layout)
    {
        // The logo is part of the layout, so it scales with the rest of the UI. It rattles along with every kick of the duel
        if (layout.TryGet<Image>("logo", out var logo) && _duel is { } duel) duel.Struck += () => logo.Shake(8.0f, 0.35f);

        // The menu is laid out in Assets/ui/layouts/main_menu.hor, what its buttons do is decided here
        AddButton(layout, "btn_pvp", () => Play(MatchMode.Pvp));
        AddButton(layout, "btn_practice", () => Play(MatchMode.Practice));
        AddButton(layout, "btn_host", HostServer);
        AddButton(layout, "btn_join", () => GoTo(new JoinServerScene()));
        AddButton(layout, "btn_options", () => GoTo(new OptionsScene()));

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
