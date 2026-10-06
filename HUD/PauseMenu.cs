using System;

using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.Input2;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.HUD;

/// <summary>
/// The menu that comes up when somebody presses start in the middle of a fight. Carry on, start the match over or sod off to the main menu.
/// This only is the menu. Holding the fight still while it is up is the job of the scene, which asks <see cref="HoldsFight"/>.
/// Only fights on one machine get one, there is no pausing somebody who is sat at another.
/// </summary>
internal sealed class PauseMenu : IGameComponent
{
    private const string HINT = "[icon:dpad] choose    [icon:pad_a] pick    [icon:pad_b] carry on";

    // The longest (in seconds) the fight waits for everybody to let go of their buttons once the menu is gone
    private const float LONGEST_RELEASE_WAIT = 0.5f;

    private readonly ButtonList _buttons = new();
    private UICompositor _compositor = null!;
    private UILayout _layout = null!;
    private Label _hint = null!;

    private float _releaseWait;
    private bool _leaving;

    /// <summary>
    /// Whether the menu is up.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Whether the fight is to hold still. That is while the menu is up, and for a moment after while somebody is still holding the button they closed it with.
    /// </summary>
    public bool HoldsFight => IsOpen || _releaseWait > 0.0f;

    /// <summary>
    /// Asked when somebody presses start, false keeps the menu shut. For whoever knows whether this is a good moment.
    /// </summary>
    public Func<bool>? CanOpen { get; init; }

    /// <summary>
    /// Called when the menu comes up and when it goes away again.
    /// </summary>
    public Action? Opened { get; init; }
    public Action? Closed { get; init; }

    /// <summary>
    /// What starting the match over and leaving for the main menu come to, the menu only has the buttons for them.
    /// </summary>
    public required Action Rematch { get; init; }
    public required Action Quit { get; init; }

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Pause Menu";
    public Entity Parent { get; set; } = null!;

    public void Initialize()
    {
        var camera = new Camera2D(GameEngine.Instance.WindowManager.ViewportSize);
        camera.Render(0);

        (_layout, _compositor) = MenuLayouts.Load(camera, MenuLayouts.PAUSE_MENU);
        _compositor.Initialize();

        AddButton("btn_resume", Close);
        AddButton("btn_rematch", () => Leave(Rematch));
        AddButton("btn_quit", () => Leave(Quit));

        _hint = _layout.Get<Label>("hint");
    }

    private void AddButton(string name, Action pressed)
    {
        var button = _layout.Get<Button>(name);
        button.OnPressed = pressed;

        _buttons.Add(button);
    }

    private void Open()
    {
        IsOpen = true;

        _buttons.Select(0);
        _layout.PlayIntros();

        Opened?.Invoke();
    }

    private void Close()
    {
        IsOpen = false;
        _releaseWait = LONGEST_RELEASE_WAIT;

        Closed?.Invoke();
    }

    /// <summary>
    /// Helper method for the buttons that end the fight. The menu stays up (and the fight frozen) until the scene has gone.
    /// </summary>
    private void Leave(Action how)
    {
        if (_leaving) return;

        _leaving = true;
        how();
    }

    public void UpdateState(float dt)
    {
        if (_leaving) return;

        if (_releaseWait > 0.0f)
        {
            // The button that closed the menu is most likely somebody's kick as well. The fight waits until it is let go of, or it would come out
            _releaseWait = AnybodyHolding() ? _releaseWait - dt : 0.0f;
            return;
        }

        if (!IsOpen)
        {
            if (StartPressed() && CanOpen?.Invoke() != false) Open();
            return;
        }

        _hint.Text = ButtonGlyphs.Localize(HINT, GameInput.Manager.LastUsed);
        _compositor.UpdateState(dt);

        // Whoever has a gamepad gets a say, it is not just player one who needs a piss
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            if (gamepad.WasPressed(GamepadInput.B) || gamepad.WasPressed(GamepadInput.Start))
            {
                Close();
                return;
            }

            _buttons.Move(MenuInput.Vertical(gamepad));

            if (gamepad.WasPressed(GamepadInput.A))
            {
                _buttons.Press();
                return;
            }
        }
    }

    private static bool StartPressed()
    {
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (gamepad.IsConnected && gamepad.WasPressed(GamepadInput.Start)) return true;
        }

        return false;
    }

    private static bool AnybodyHolding()
    {
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (gamepad.IsConnected && gamepad.AnyDown) return true;
        }

        return false;
    }

    public void UpdatePhysics(float dt) { }

    public void Render(float dt, object? obj = null)
    {
        if (IsOpen) _compositor.Render(dt);
    }
}
