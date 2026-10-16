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

namespace Fighter2D.Menus;

/// <summary>
/// The first scene of the game, where the kind of fight is picked (or the options).
/// Behind the menu two fighters go at each other forever, see <see cref="MenuDuel"/>.
/// The gamepad walks the buttons through the engine's navigator, the same one the pause menu uses, and a line under
/// the buttons says what the one it is on does. Quitting asks first, nobody wants to lose the game to a slip of the thumb.
/// </summary>
internal class MainMenuScene : MenuScene
{
    // Where the fighters of the background meet and the ground they stand on, in pixels of the window
    private static readonly Vector2 DuelCenter = new(330, -330);

    protected override string LayoutFile => MenuLayouts.MAIN_MENU;
    protected override Vector2 CameraSize => Engine.WindowManager.ViewportSize;
    protected override System.Drawing.Color ClearColor => System.Drawing.Color.Black;

    private UILayout _layout = null!;
    private UINavigator _nav = null!;
    private MenuDuel? _duel;
    private Label _description = null!;

    // What every button has to say for itself, by the button
    private readonly Dictionary<UIComponent, string> _descriptions = [];

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

    private void ShowMessage(string message) => Hint?.Say(message);

    protected override void BuildActions()
    {
        // Up and down walk the menu, A is whatever the gamepad is on. Whoever presses it is player one of the fight it starts
        Actions.Directions("hints.choose", (_, _, down) => _nav.Move(0, down));
        Actions.Add(GamepadInput.A, "hints.pick", Pick);
    }

    private void Pick(Gamepad gamepad)
    {
        _pressedBy = gamepad.Slot;
        _nav.Activate();
        _pressedBy = null;
    }

    protected override void UpdateMenu(float dt) { }
}
