using System;
using System.Linq;
using System.Numerics;

using Horizon.Input;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the player sets the game up the way they like it. The window, the look of the game and what a fight shows on top of itself.
/// Every option is a row of the screen and takes hold the moment it is changed, there is nothing to confirm. They are all kept in <see cref="GameOptions"/>.
/// </summary>
/// <param name="selected">The row the gamepad starts on, for when the screen had to be made again half way through (see <see cref="Reload"/>).</param>
internal class OptionsScene(int selected = 0) : MenuScene
{
    private const string HINT = "[icon:dpad] choose and change    [icon:pad_b] back";

    protected override string LayoutFile => MenuLayouts.OPTIONS;
    protected override float InputDelay => 0.2f;

    private readonly SettingList _settings = new();
    private Label _hint = null!, _description = null!;
    private UICompositor _ui = null!;

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/options.hor, what its rows choose between is decided here.
        // So is the line about each of them. Labels don't wrap, so the ones that run long are broken in two by hand
        _ui = layout.Module.Compositor;
        _hint = layout.Get<Label>("hint");
        _description = layout.Get<Label>("description");

        AddWindowOptions(layout);
        AddLookOptions(layout);
        AddFightOptions(layout);

        _settings.Changed = GameOptions.Save;
        _settings.Select(selected);
    }

    private void AddWindowOptions(UILayout layout)
    {
        _settings.Add(layout, "display", [false, true], GameOptions.Fullscreen, fullscreen => fullscreen ? "Fullscreen" : "Windowed",
            fullscreen =>
            {
                GameOptions.Fullscreen = fullscreen;
                ResizeWindow();
            },
            "Fullscreen takes up the whole screen,\nat whatever resolution that screen is set to.");

        _settings.Add(layout, "resolution", FittingResolutions(), GameOptions.Resolution, size => $"{size.X:0} x {size.Y:0}",
            size =>
            {
                GameOptions.Resolution = size;

                // Nothing to see of it while the game is fullscreen, it is remembered for when it isn't
                if (!GameOptions.Fullscreen) ResizeWindow();
            },
            "How big the window is.\nIt makes no difference while the game is fullscreen.");

        _settings.AddSwitch(layout, "vsync", GameOptions.VSync,
            on =>
            {
                GameOptions.VSync = on;
                Engine.WindowManager.Apply(GameOptions.Display);
            },
            "Waits for the screen before showing a frame,\nso none of them is torn in two.");

        _settings.Add(layout, "frame_limit", GameOptions.FrameLimits, GameOptions.FrameLimit, DescribeFrameLimit,
            limit =>
            {
                GameOptions.FrameLimit = limit;
                Engine.WindowManager.Apply(GameOptions.Display);
            },
            $"The most frames that are drawn a second.\nThe fight runs at {GameOptions.MIN_FRAME_LIMIT} ticks a second, so that is as low as it goes.");
    }

    private void AddLookOptions(UILayout layout)
    {
        _settings.Add(layout, "transitions", Enum.GetValues<TransitionStyle>(), GameOptions.Transitions, DescribeTransitions,
            style =>
            {
                GameOptions.Transitions = style;
                Screen.ApplyTransitions();

                // Shown off right away, by going from this screen to itself
                Reload(Screen.BetweenMenus);
            },
            "How one screen hands over to the next.\nMixed melts the menus into each other and rots a fight in and out.");

        _settings.AddSwitch(layout, "crt", GameOptions.Crt,
            on =>
            {
                GameOptions.Crt = on;
                Screen.ApplyOptions(Canvas, _ui);
            },
            "Draws the game like it is on a crusty old picture tube.");

        _settings.AddSwitch(layout, "motion_blur", GameOptions.MotionBlur,
            on =>
            {
                GameOptions.MotionBlur = on;
                Screen.ApplyOptions(Canvas, _ui);
            },
            "Smears whatever moves along the way it is going,\nwhich hides that pixel art moves in steps.");
    }

    private void AddFightOptions(UILayout layout)
    {
        _settings.AddSwitch(layout, "input_display", GameOptions.InputDisplay, on => GameOptions.InputDisplay = on,
            "Shows what both players are pressing down the sides of a fight.");

        _settings.AddSwitch(layout, "hit_callouts", GameOptions.HitCallouts, on => GameOptions.HitCallouts = on,
            "Calls out what every attack came to.\nHIT, COUNTER HIT, PUNISH, BLOCKED or WHIFF.");

        _settings.AddSwitch(layout, "frame_data", GameOptions.FrameData, on => GameOptions.FrameData = on,
            "Adds the startup of a move to the callouts, and who gets to act first after it.\nFor the lab rats.");

        _settings.AddSwitch(layout, "hitboxes", GameOptions.Hitboxes, on => GameOptions.Hitboxes = on,
            "Draws the collision of a fight over it.");
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
        if (limit == GameOptions.NO_FRAME_LIMIT) return "No limit";

        return limit == GameOptions.MIN_FRAME_LIMIT ? $"{limit} (the tick rate)" : limit.ToString();
    }

    private static string DescribeTransitions(TransitionStyle style) => style switch
    {
        TransitionStyle.Mixed => "Mixed",
        TransitionStyle.Off => "Off",
        _ => style.ToString()
    };

    /// <summary>
    /// Helper method for whatever changes the size of the window. Nothing of this screen fits the new one, so it is made again.
    /// The window changes at the start of the next frame and so do the scenes, which is why the new one finds the window the right size.
    /// </summary>
    private void ResizeWindow()
    {
        Engine.WindowManager.Apply(GameOptions.Display);
        Reload(null);
    }

    /// <summary>
    /// Helper method to swap this screen for a fresh one with the gamepad on the same row.
    /// </summary>
    private void Reload(Horizon.Engine.SceneTransition? transition)
    {
        GoTo(new OptionsScene(_settings.Selected), transition);
    }

    protected override void UpdateMenu(float dt)
    {
        // Nobody has picked a gamepad yet, so the screen listens to all of them
        _hint.Text = ButtonGlyphs.Localize(HINT, GameInput.Manager.LastUsed);
        _description.Text = _settings.Hint;

        if (!InputReady) return;

        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            if (gamepad.WasPressed(GamepadInput.B))
            {
                GoTo(new MainMenuScene());
                return;
            }

            _settings.Move(MenuInput.Vertical(gamepad));

            // A step can swap this screen for a new one, which is the end of this one
            int horizontal = MenuInput.Horizontal(gamepad);
            if (horizontal != 0)
            {
                _settings.Step(horizontal);
                return;
            }
        }
    }
}
