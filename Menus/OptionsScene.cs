using Horizon.Content;
using System;
using System.Linq;
using System.Numerics;

using Horizon.Engine.Components;
using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.Menus;

/// <summary>
/// Scene where the player sets the game up the way they like it. The window, the look of the game and what a fight shows on top of itself.
/// The options come in four tabs the bumpers flip between (the window, how a fight is drawn, the look of the menus and the fight itself), so nobody has to scroll through the lot to get to the one they want.
/// Every option is a row of its tab and takes hold the moment it is changed, there is nothing to confirm. They are all kept in <see cref="GameOptions"/>.
/// What the row the gamepad is on is and does is written in a box next to the rows. Y on a gamepad opens its bindings (<see cref="BindingsScene"/>),
/// which is the way to them without setting a fight up first.
/// </summary>
/// <param name="tab">The tab that is open to begin with, and the row the gamepad starts on in it: for when the screen had to be made again half way through (see <see cref="Reload"/>).</param>
internal class OptionsScene(int tab = 0, int selected = 0) : MenuScene
{

    protected override string LayoutFile => MenuLayouts.OPTIONS;
    protected override float InputDelay => 0.2f;

    // A list of rows for every page of the tabs (the gamepad goes down the rows of the open one), and the tabs themselves
    private readonly SettingList[] _pages = [new(), new(), new(), new()];
    private SettingList _settings = null!;
    private TabPanel _tabs = null!;

    private Label _description = null!, _aboutTitle = null!;
    private UICompositor _ui = null!;

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/options.hor, what its rows choose between is decided here.
        // So is the line about each of them, which wraps by itself in the layout
        _ui = layout.Module.Compositor;
        _description = layout.Get<Label>("description");
        _aboutTitle = layout.Get<Label>("about_title");

        // For the mouse, which has no Y and no B. The bindings are those of whichever gamepad was touched last
        layout.Get<Horizon.UI.Components.Button>("btn_back").OnPressed = Back;
        layout.Get<Horizon.UI.Components.Button>("btn_bindings").OnPressed = () =>
        {
            Gamepad? gamepad = GameInput.Manager.LastUsed ?? GameInput.Manager.Gamepads.FirstOrDefault(each => each.IsConnected);
            if (gamepad is not null) OpenBindings(gamepad);
            else Hint?.Label.Shake(6.0f);
        };

        _module = layout.Module;
        layout.Get<Horizon.UI.Components.Button>("btn_defaults").OnPressed = AskDefaults;

        _settings = _pages[0];
        AddWindowOptions(layout);
        _settings = _pages[1];
        AddGraphicsOptions(layout);
        _settings = _pages[2];
        AddLookOptions(layout);
        _settings = _pages[3];
        AddFightOptions(layout);

        foreach (SettingList list in _pages) list.Changed = GameOptions.Save;

        // The tabs do the flipping and the drawing, all we do is point the gamepad at the rows of the open page.
        // A click on a tab lands here as well as the bumpers
        _tabs = layout.Get<TabPanel>("tabs");
        _tabs.OnChanged = page =>
        {
            _settings = _pages[page];
            _settings.Select(0);
            _tabs.Punch(0.04f, 0.15f);
        };

        _tabs.Selected = tab;
        _settings = _pages[_tabs.Selected];
        _settings.Select(selected);
    }

    private void AddWindowOptions(UILayout layout)
    {
        _settings.Add(layout, "display", [false, true], GameOptions.Fullscreen, fullscreen => Localisation.Get(fullscreen ? "options.fullscreen" : "options.windowed"),
            fullscreen =>
            {
                GameOptions.Fullscreen = fullscreen;
                ResizeWindow();
            },
            Localisation.Get("options.about_display"));

        _settings.Add(layout, "resolution", FittingResolutions(), GameOptions.Resolution, size => Localisation.Format("options.size", ("width", size.X.ToString("0")), ("height", size.Y.ToString("0"))),
            size =>
            {
                GameOptions.Resolution = size;

                // Nothing to see of it while the game is fullscreen, it is remembered for when it isn't
                if (!GameOptions.Fullscreen) ResizeWindow();
            },
            Localisation.Get("options.about_resolution"));

        _settings.AddSwitch(layout, "vsync", GameOptions.VSync,
            on =>
            {
                GameOptions.VSync = on;
                Engine.WindowManager.Apply(GameOptions.Display);
            },
            Localisation.Get("options.about_vsync"));

        _settings.Add(layout, "frame_limit", GameOptions.FrameLimits, GameOptions.FrameLimit, DescribeFrameLimit,
            limit =>
            {
                GameOptions.FrameLimit = limit;
                Engine.WindowManager.Apply(GameOptions.Display);
            },
            Localisation.Format("options.about_frame_limit", ("ticks", GameOptions.MIN_FRAME_LIMIT)));

        _settings.Add(layout, "performance", Enum.GetValues<PerformanceDetail>(), GameOptions.Performance, detail => Localisation.Get("options.performance_" + detail.ToString().ToLowerInvariant()),
            detail =>
            {
                GameOptions.Performance = detail;
                if (Screen.Performance is { } overlay) overlay.Detail = detail;
            },
            Localisation.Get("options.about_performance"));
    }

    private void AddLookOptions(UILayout layout)
    {
        // In the order they are in on the screen, the gamepad goes down the rows by the order they were added in
        _settings.Add(layout, "gui_scale", GameOptions.GuiScales, GameOptions.GuiScale, scale => Localisation.Format("options.percent", ("percent", (scale * 100).ToString("0"))),
            scale =>
            {
                GameOptions.GuiScale = scale;

                // Shown off on this screen straight away, every other one is made with it
                _ui.Scale = scale;
            },
            Localisation.Get("options.about_gui_scale"));

        // The skin, and the themes of whichever skin it is. Another skin has other themes, so the screen is made
        // again when it changes, which shows the new one off as well
        var skins = Horizon.UI.Skinning.UISkins.Available.ToArray();
        var skin = Horizon.UI.Skinning.UISkins.Current;
        _settings.Add(layout, "ui_skin", skins, skin, choice => choice.Title,
            choice =>
            {
                GameOptions.UiSkin = choice.Name;
                GameOptions.UiTheme = choice.UsualTheme ?? string.Empty;
                Screen.ApplySkin();
                Reload(null);
            },
            Localisation.Get("options.about_ui_skin"));

        string[] themes = skin.Themes.Count > 0 ? [.. skin.Themes] : [Localisation.Get("options.one_look")];
        string theme = Horizon.UI.Skinning.UISkins.Theme ?? skin.UsualTheme ?? themes[0];
        _settings.Add(layout, "ui_theme", themes, themes.Contains(theme) ? theme : themes[0], name => name,
            name =>
            {
                if (skin.Themes.Count == 0) return;

                GameOptions.UiTheme = name;
                Screen.ApplySkin();
            },
            Localisation.Get("options.about_ui_theme"));

        _settings.Add(layout, "transitions", Enum.GetValues<TransitionStyle>(), GameOptions.Transitions, DescribeTransitions,
            style =>
            {
                GameOptions.Transitions = style;
                Screen.ApplyTransitions();

                // Shown off right away, by going from this screen to itself
                Reload(Screen.BetweenMenus);
            },
            Localisation.Get("options.about_transitions"));

    }

    /// <summary>
    /// Helper method to add how a fight is drawn, the light and what is laid over the picture. Apart from the window
    /// and from the look of the menus, these are the ones that cost the GPU something.
    /// </summary>
    private void AddGraphicsOptions(UILayout layout)
    {
        _settings.AddSwitch(layout, "crt", GameOptions.Crt,
            on =>
            {
                GameOptions.Crt = on;
                Screen.ApplyOptions(Canvas, _ui);
            },
            Localisation.Get("options.about_crt"));

        _settings.AddSwitch(layout, "bloom", GameOptions.Bloom,
            on =>
            {
                GameOptions.Bloom = on;
                Screen.ApplyOptions(Canvas, _ui);
            },
            Localisation.Get("options.about_bloom"));

        _settings.AddSwitch(layout, "pathtraced", GameOptions.RenderPathtraced,
            on =>
            {
                GameOptions.RenderPathtraced = on;
                Screen.ApplyOptions(Canvas, _ui);
            },
            Localisation.Get("options.about_pathtraced"));

        _settings.Add(layout, "light_quality", Enum.GetValues<LightQuality>(), GameOptions.LightQuality, quality => Localisation.Get("options.light_" + quality.ToString().ToLowerInvariant()),
            quality =>
            {
                GameOptions.LightQuality = quality;
                Screen.ApplyOptions(Canvas, _ui);
            },
            Localisation.Get("options.about_light_quality"));
    }

    private void AddFightOptions(UILayout layout)
    {
        _settings.Add(layout, "camera_zoom", GameOptions.CameraZooms, GameOptions.CameraZoom, DescribeZoom, zoom => GameOptions.CameraZoom = zoom,
            Localisation.Get("options.about_camera_zoom"));

        _settings.AddSwitch(layout, "input_display", GameOptions.InputDisplay, on => GameOptions.InputDisplay = on,
            Localisation.Get("options.about_input_display"));

        _settings.AddSwitch(layout, "hit_callouts", GameOptions.HitCallouts, on => GameOptions.HitCallouts = on,
            Localisation.Get("options.about_hit_callouts"));

        _settings.AddSwitch(layout, "frame_data", GameOptions.FrameData, on => GameOptions.FrameData = on,
            Localisation.Get("options.about_frame_data"));

        _settings.Add(layout, "controls", Enum.GetValues<ControlsShown>(), GameOptions.Controls,
            shown => Localisation.Get("options.controls_" + shown.ToString().ToLowerInvariant()), shown => GameOptions.Controls = shown,
            Localisation.Get("options.about_controls"));

        _settings.AddSwitch(layout, "hitboxes", GameOptions.Hitboxes, on => GameOptions.Hitboxes = on,
            Localisation.Get("options.about_hitboxes"));
    }

    /// <summary>
    /// Helper method to list the sizes a window can be on the screen it is on. Whatever it is set to right now is always one of them.
    /// </summary>
    private Vector2[] FittingResolutions()
    {
        Vector2 screen = Engine.WindowManager.ScreenSize;

        var fitting = GameOptions.Resolutions.Where(size => size.X <= screen.X && size.Y <= screen.Y).ToList();
        if (!fitting.Contains(GameOptions.Resolution)) fitting.Add(GameOptions.Resolution);

        return [.. fitting.OrderBy(size => size.X)];
    }

    private static string DescribeFrameLimit(int limit)
    {
        if (limit == GameOptions.NO_FRAME_LIMIT) return Localisation.Get("options.no_limit");

        return limit == GameOptions.MIN_FRAME_LIMIT ? Localisation.Format("options.tick_rate", ("limit", limit)) : limit.ToString();
    }

    private static string DescribeZoom(float zoom) => Localisation.Get(zoom switch
    {
        <= 0.6f => "options.zoom_close",
        <= 0.75f => "options.zoom_normal",
        <= 0.9f => "options.zoom_far",
        _ => "options.zoom_furthest"
    });

    private static string DescribeTransitions(TransitionStyle style) => Localisation.Get("options.transitions_" + style.ToString().ToLowerInvariant());

    /// <summary>
    /// Helper method for whatever changes the size of the window. Nothing of this screen fits the new one, so it is made again.
    /// The window changes at the start of the next frame and so do the scenes, which is why the new one finds the window the right size.
    /// </summary>
    private void ResizeWindow()
    {
        Engine.WindowManager.Apply(GameOptions.Display);
        Reload(null);
    }

    private Horizon.UI.UIModule _module = null!;

    /// <summary>
    /// Asks before the tab that is open goes back to how the game came, it is a lot of rows to set again by hand.
    /// </summary>
    private void AskDefaults()
    {
        string tab = Localisation.Get("options.tab_" + ((OptionsTab)_tabs.Selected).ToString().ToLowerInvariant());
        Horizon.UI.Components.UIDialog.Show(_module, Localisation.Get("options.defaults_title"), Localisation.Format("options.defaults_question", ("tab", tab)),
            new Horizon.UI.Components.DialogChoice(Localisation.Get("options.defaults_confirm"), ResetToDefaults),
            new Horizon.UI.Components.DialogChoice(Localisation.Get("common.stay")));
    }

    /// <summary>
    /// Helper method to put the open tab back to its defaults, see to whatever they change (the window, the skin, the
    /// look of the fight), and make the screen again so its rows say the new values.
    /// </summary>
    private void ResetToDefaults()
    {
        var tab = (OptionsTab)_tabs.Selected;
        GameOptions.ResetToDefaults(tab);
        GameOptions.Save();

        switch (tab)
        {
            case OptionsTab.Display:
                Engine.WindowManager.Apply(GameOptions.Display);
                if (Screen.Performance is { } overlay) overlay.Detail = GameOptions.Performance;
                break;

            case OptionsTab.Graphics:
                Screen.ApplyOptions(Canvas, _ui);
                break;

            case OptionsTab.Look:
                Screen.ApplySkin();
                Screen.ApplyTransitions();
                _ui.Scale = GameOptions.GuiScale;
                break;
        }

        Reload(null);
    }

    /// <summary>
    /// Helper method for the way out. Back to whatever the options were opened over (a fight, from its pause menu),
    /// which is underneath as it was left, or to the main menu when they were opened from there.
    /// </summary>
    private void Back()
    {
        if (!Return(Screen.BetweenMenus)) GoTo(new MainMenuScene());
    }

    /// <summary>
    /// Helper method to swap this screen for a fresh one with the gamepad on the same row. A swap leaves whatever
    /// is underneath where it is, so the way back is still there afterwards.
    /// </summary>
    /// <summary>
    /// What is behind the options is whatever they were opened over, the fight or the main menu, which is still
    /// drawn (it was put there with SceneBelow.Running) and blurs itself the way a fight does behind its pause
    /// menu, with the blur on its own renderer. So the options bring no picture of their own and leave their
    /// renderer out, it would cover the scene under them. Opened on their own, with nothing under them, they have
    /// the backdrop every menu has.
    /// </summary>
    protected override void BuildBackdrop()
    {
        // Whoever opened the options over their own scene says so. Asking the engine whether there is a scene
        // underneath is too early here, a scene that comes in through a transition is set up before it is put on
        // top of anything. One that takes over from another options screen (see Reload) can ask
        if (!OverScene && !CanReturn)
        {
            base.BuildBackdrop();
            return;
        }

        // The renderer is switched back on as it is set up like everything is, so it is put out again on the
        // first update (see UpdateMenu) and not here
        _overAnother = true;
    }

    // Whether the options were opened over another scene, which is then what is behind them
    private bool _overAnother;

    /// <summary>
    /// Set by whoever opens the options on top of their own scene (Open, with the scene underneath still drawn),
    /// which is then what is behind them, blurred. Left alone the options bring the backdrop every menu has.
    /// </summary>
    public bool OverScene { get; init; }

    private void Reload(Horizon.Engine.SceneTransition? transition)
    {
        GoTo(new OptionsScene(_tabs.Selected, _settings.Selected), transition);
    }

    /// <summary>
    /// Off to the bindings of a gamepad, opened over this screen, which is back on the row we were on when they are done.
    /// </summary>
    private void OpenBindings(Gamepad gamepad)
    {
        Open(new BindingsScene(gamepad.Slot, Localisation.Get("bindings.title_options")), Screen.BetweenMenus);
    }

    protected override void UpdateMenu(float dt)
    {
        if (_overAnother && Canvas.Enabled) Canvas.Enabled = false;

        // What a row is about, as it is written. The box wraps it by itself and reads it as markdown, so a line break in
        // it is meant (a heading for every choice, say), the ones that were only there for a wider screen are gone
        _aboutTitle.Text = _settings.Title;
        _description.Text = _settings.Hint;

    }

    protected override void BuildActions()
    {
        // Up and down walk the rows, left and right change the one the gamepad is on (which can swap this screen for a new one)
        Actions.Directions("hints.choose_and_change", (_, right, down) =>
        {
            _settings.Move(down);
            if (right != 0) _settings.Step(right);
        });

        // The bumpers flip between the tabs
        Actions.Pair(GamepadInput.LeftBumper, GamepadInput.RightBumper, "hints.tabs", (_, step) =>
        {
            if (step > 0) _tabs.Next();
            else _tabs.Previous();
        });

        Actions.Add(GamepadInput.Y, "hints.bindings", OpenBindings);
        Actions.Add(GamepadInput.X, "hints.defaults", AskDefaults);
        Actions.Add(GamepadInput.B, "hints.back", Back);
    }
}
