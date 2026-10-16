using Horizon.Content;
using System;
using System.Numerics;

using Horizon.Rendering;
using Horizon.Input;
using Horizon.Rendering.Spriting;
using Horizon.UI;
using Horizon.UI.Components;


using Button = Horizon.UI.Components.Button;
using Horizon.Graphics;

namespace Fighter2D.Scenes;

/// <summary>
/// The first scene of the game, where the kind of fight is picked (or the options).
/// Behind the menu two fighters go at each other forever, see <see cref="MenuDuel"/>.
/// The gamepad walks the buttons through the engine's navigator, the same one the pause menu uses, and a line under
/// the buttons says what the one it is on does. Quitting asks first, nobody wants to lose the game to a slip of the thumb.
/// </summary>
internal class MainMenuScene : MenuScene
{
    // Which buttons do what, out of the language that is loaded like every other text of the game
    private static string Hint => Localisation.Get("main_menu.hint");

    // Where the fighters of the background meet and the ground they stand on, in pixels of the window
    private static readonly Vector2 DuelCenter = new(330, -330);

    protected override string LayoutFile => MenuLayouts.MAIN_MENU;
    protected override Vector2 CameraSize => Engine.WindowManager.ViewportSize;
    protected override System.Drawing.Color ClearColor => System.Drawing.Color.Black;

    private UILayout _layout = null!;
    private UINavigator _nav = null!;
    private MenuDuel? _duel;
    private Label _hint = null!, _description = null!;

    // What every button has to say for itself, by the button
    private readonly Dictionary<UIComponent, string> _descriptions = [];

    // Whether the hint is busy saying something else than which buttons to press
    private bool _hintIsMessage;

    // The gamepad that pressed the button being acted on, null for a click of the mouse
    private int? _pressedBy;

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
        _layout = layout;

        // The logo is part of the layout, so it scales with the rest of the UI. It rattles along with every kick of the duel
        if (layout.TryGet<Image>("logo", out var logo) && _duel is { } duel) duel.Struck += () => logo.Shake(8.0f, 0.35f);

        // The menu is laid out in Assets/ui/layouts/main_menu.hor, what its buttons do is decided here
        AddButton(layout, "btn_pvp", () => Play(MatchMode.Pvp), "main_menu.about_pvp");
        AddButton(layout, "btn_practice", () => Play(MatchMode.Practice), "main_menu.about_practice");
        AddButton(layout, "btn_host", HostServer, "main_menu.about_host");
        AddButton(layout, "btn_join", () => GoTo(new JoinServerScene()), "main_menu.about_join");
        AddButton(layout, "btn_options", () => Open(new OptionsScene { OverScene = true }, Screen.BetweenMenus, Horizon.Engine.Components.SceneBelow.Running), "main_menu.about_options");
        AddButton(layout, "btn_quit", AskToQuit, "main_menu.about_quit");

        _hint = layout.Get<Label>("hint");
        _description = layout.Get<Label>("description");
        layout.Get<Label>("version").Text = Constants.VERSION_LABEL;

        // The navigator lights the button the gamepad is on, and the line under the buttons follows it
        _nav = layout.Module.Navigation;
        _nav.Changed = selected => _description.Text = selected is not null && _descriptions.TryGetValue(selected, out string? about) ? about : string.Empty;
        _nav.SelectFirst();
    }

    /// <param name="description">The key of what the button has to say for itself, see Assets/ui/localisation.</param>
    private void AddButton(UILayout layout, string name, Action pressed, string description)
    {
        var button = layout.Get<Button>(name);
        button.OnPressed = pressed;
        _descriptions[button] = Localisation.Get(description);
    }

    private void Play(MatchMode mode) => GoTo(new FightSetupScene(Claim(new MatchSetup(mode))));

    /// <summary>
    /// Whoever picked the fight with a gamepad is player one with it, they don't get asked to press a button on it all over again.
    /// </summary>
    private MatchSetup Claim(MatchSetup setup)
    {
        if (_pressedBy is { } slot) setup.Slots.Add(slot);
        return setup;
    }

    private void HostServer()
    {
        // The server is up before the lobby is, whoever joins waits there with us
        var session = NetSession.Host();
        if (session.HasFailed)
        {
            session.Dispose();
            ShowMessage(Localisation.Format("main_menu.port_taken", ("port", NetSession.PORT)));
            return;
        }

        GoTo(new FightSetupScene(Claim(MatchSetup.Online(session))));
    }

    private void AskToQuit()
    {
        UIDialog.Show(_layout.Module, Localisation.Get("main_menu.quit_title"), Localisation.Get("main_menu.quit_question"),
            new DialogChoice(Localisation.Get("main_menu.quit_confirm"), () => Engine.WindowManager.Window.Close()),
            new DialogChoice(Localisation.Get("common.stay")));
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
        if (!_hintIsMessage) _hint.Text = ButtonGlyphs.Localize(Hint, GameInput.Manager.LastUsed);

        if (!InputReady) return;

        // Nobody has picked a gamepad yet, so the menu listens to all of them
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            // A question that is up takes the gamepad, B is the answer that changes nothing
            if (_layout.Module.Dialog is { } dialog && gamepad.WasPressed(GamepadInput.B))
            {
                dialog.Cancel();
                return;
            }

            // Up and down walk the menu, left and right walk a question's buttons, which sit side by side
            int across = MenuInput.Horizontal(gamepad);
            if (across != 0) _nav.Move(across, 0);
            else _nav.Move(0, MenuInput.Vertical(gamepad));

            if (MenuInput.ConfirmPressed(gamepad))
            {
                _pressedBy = gamepad.Slot;
                _nav.Activate();
                _pressedBy = null;
                return;
            }
        }
    }
}
