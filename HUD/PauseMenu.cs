using System;

using Horizon.Core.Components;
using Horizon.Core.Threading;
using Horizon.Input;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.HUD;

/// <summary>
/// The menu that comes up when somebody presses start in the middle of a fight. Carry on, start the match over or sod off to the main menu.
/// A practice fight gets the training options under that (see <see cref="TrainingMode"/>).
/// This only is the menu. Holding the fight still while it is up is the job of the scene, which asks <see cref="HoldsFight"/>.
/// Online a pause is a pause for both machines. Whoever didn't press start gets told who did and waits for them to carry on,
/// only the one who paused can unpause (see <see cref="HeldByOther"/>).
/// Nobody is dropped straight back into the fight either. Once the pause is over (or the data has been reloaded) it counts
/// down from three first, and the fight stays held until it gets to the end of that.
/// The gamepad walks the menu through the engine's navigator, which is what lets it step the selectors as well as press the buttons.
/// </summary>
internal sealed class PauseMenu : GameComponent
{
    private const string HINT = "[icon:dpad] choose    [icon:pad_a] pick    [icon:pad_b] carry on";
    private const string TRAINING_HINT = "[icon:dpad] choose and change    [icon:pad_a] pick    [icon:pad_b] carry on";

    // The layout is four layers. The menu of whoever paused, the training options under it (practice only), what the other
    // machine shows while it waits for them, and the countdown both of them get before the fight is back on
    private const string MENU_LAYER = "menu";
    private const string TRAINING_LAYER = "training";
    private const string WAITING_LAYER = "waiting";
    private const string COUNTDOWN_LAYER = "countdown";

    // What the countdown counts from, a second for each
    private const int COUNTDOWN_FROM = 3;

    private UICompositor _compositor = null!;
    private UILayout _layout = null!;
    private UINavigator _nav = null!;
    private Button _resume = null!;
    private Label _hint = null!, _count = null!;

    // How much of the countdown is left in seconds, and the number of it that is on screen
    private float _countdown;
    private int _shownCount;

    // Whether any of it is up, as of every tick: what the frames drawn alongside the simulation go by. Asking the menu
    // itself from a frame is asking something that may be halfway through changing its mind
    private readonly Snapshot<bool> _shown = new();

    // Whether the fight was paused (by anybody) on the last update, which is how the end of a pause is noticed
    private bool _wasPaused;
    private bool _leaving;

    /// <summary>
    /// Whether the menu is up.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Whether the other machine has the fight paused, asked on every update. Null for a fight that has no other machine.
    /// </summary>
    public Func<bool>? HeldByOther { get; init; }

    private bool Waiting => HeldByOther?.Invoke() == true;

    /// <summary>
    /// Whether there is anything of the pause on screen, the menu or the wait for the other player.
    /// </summary>
    public bool IsShowing => IsOpen || Waiting;

    /// <summary>
    /// Whether the fight is to hold still. That is while the menu is up or the other machine has paused,
    /// and after that for as long as the countdown takes.
    /// </summary>
    public bool HoldsFight => IsShowing || _countdown > 0.0f;

    /// <summary>
    /// Counts down from three before the fight carries on. The end of a pause does this by itself,
    /// this is for whoever else has held the fight up (the data being reloaded).
    /// </summary>
    public void StartCountdown()
    {
        _countdown = COUNTDOWN_FROM;
        _shownCount = 0;
    }

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
    /// Without a rematch (online there is none) the button for it isn't there.
    /// </summary>
    public Action? Rematch { get; init; }
    public required Action Quit { get; init; }

    /// <summary>
    /// The tools of the host, a list of its own under the menu. Reading all the data again from disk and drawing the hitboxes.
    /// Null for whoever isn't the host (the machine that joined an online fight), who doesn't get the list at all.
    /// </summary>
    public Action? ReloadData { get; init; }
    public Func<bool>? ToggleHitboxes { get; init; }

    /// <summary>
    /// The training options of a practice fight, null for a real one (which doesn't get them).
    /// </summary>
    public TrainingMode? Training { get; init; }

    public override void Initialize()
    {
        (_layout, _compositor) = MenuLayouts.Load(MenuLayouts.PAUSE_MENU);
        _compositor.Initialize();

        _resume = WireButton("btn_resume", Close);

        if (Rematch is { } rematch) WireButton("btn_rematch", () => Leave(rematch));
        else _layout.Get<Button>("btn_rematch").Visible = false;

        WireButton("btn_quit", AskToQuit);
        WireHostTools();
        WireTraining();

        _hint = _layout.Get<Label>("hint");
        _count = _layout.Get<Label>("count");

        // The gamepad walks whatever is on the menu layer, the hidden layers are skipped by themselves
        _nav = _layout.Module.Navigation;

        // None of it is up until somebody pauses
        _layout.Module.SetLayerVisible(MENU_LAYER, false);
        _layout.Module.SetLayerVisible(TRAINING_LAYER, false);
        _layout.Module.SetLayerVisible(WAITING_LAYER, false);
        _layout.Module.SetLayerVisible(COUNTDOWN_LAYER, false);
    }

    /// <summary>
    /// Helper method to set up the tools of the host, or to take them off the menu for whoever isn't.
    /// </summary>
    private void WireHostTools()
    {
        var reload = _layout.Get<Button>("btn_reload");
        var hitboxes = _layout.Get<Button>("btn_hitboxes");

        if (ReloadData is null || ToggleHitboxes is null)
        {
            _layout.Get<Label>("tools_title").Visible = false;
            reload.Visible = hitboxes.Visible = false;
            return;
        }

        // The menu stays up while the data is read again, the fight that comes back from it has no pause on
        WireButton("btn_reload", ReloadData);
        WireButton("btn_hitboxes", () => hitboxes.Label = ToggleHitboxes() ? "Hitboxes: on" : "Hitboxes: off");
    }

    /// <summary>
    /// Helper method to set up the training options. What the dummy does, whether health comes back, and a reset of the positions.
    /// A fight without training mode never shows the layer they are on.
    /// </summary>
    private void WireTraining()
    {
        if (Training is not { } training) return;

        var dummy = _layout.Get<Selector>("dummy");
        dummy.Options = [.. DummyModes.All.Select(DummyModes.Describe)];
        dummy.Index = Array.IndexOf(DummyModes.All, training.Dummy);
        dummy.OnChanged = _ => training.Dummy = DummyModes.All[dummy.Index];

        var refill = _layout.Get<Selector>("refill");
        refill.Options = ["Off", "On"];
        refill.Index = training.RefillHealth ? 1 : 0;
        refill.OnChanged = _ => training.RefillHealth = refill.Index == 1;

        WireButton("btn_reset", () =>
        {
            training.ResetPositions();
            Close();
        });
    }

    /// <summary>
    /// Helper method to ask before the fight is thrown away. The question comes up over the menu and takes the gamepad with it.
    /// </summary>
    private void AskToQuit()
    {
        UIDialog.Show(_layout.Module, "Leave the fight?", "The match is lost, there is no coming back to it.",
            new DialogChoice("Leave", () => Leave(Quit)),
            new DialogChoice("Stay"));
    }

    private Button WireButton(string name, Action pressed)
    {
        var button = _layout.Get<Button>(name);
        button.OnPressed = pressed;
        return button;
    }

    private void Open()
    {
        IsOpen = true;

        // The layers have to be up before the navigator will stand on anything in them
        ShowLayers();
        _nav.Select(_resume);
        _layout.PlayIntros();

        Opened?.Invoke();
    }

    private void Close()
    {
        IsOpen = false;
        _layout.Module.Dialog?.Close();
        _nav.Select(null);

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

    private void ShowLayers()
    {
        _layout.Module.SetLayerVisible(MENU_LAYER, IsOpen);
        _layout.Module.SetLayerVisible(TRAINING_LAYER, IsOpen && Training is not null);
        _layout.Module.SetLayerVisible(WAITING_LAYER, !IsOpen && Waiting);
        _layout.Module.SetLayerVisible(COUNTDOWN_LAYER, !IsShowing && _countdown > 0.0f);
    }

    public override void UpdateState(float dt)
    {
        if (_leaving) return;

        UpdateCountdown(dt);

        // The press that opens the menu is done with, or it would shut it again on the way out of this update
        if (!IsOpen && StartPressed() && CanOpen?.Invoke() != false)
        {
            Open();
            _compositor.UpdateState(dt);
            return;
        }

        ShowLayers();

        if (!IsOpen)
        {
            // Nothing to press while waiting for the other player or for the countdown, but what they say still has to be laid out
            if (HoldsFight) _compositor.UpdateState(dt);
            return;
        }

        _hint.Text = ButtonGlyphs.Localize(Training is null ? HINT : TRAINING_HINT, GameInput.Manager.LastUsed);
        _compositor.UpdateState(dt);

        // Whoever has a gamepad gets a say, it is not just player one who needs a piss
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            // A question that is up takes the gamepad, B is the answer that changes nothing
            if (_layout.Module.Dialog is { } dialog)
            {
                if (gamepad.WasPressed(GamepadInput.B)) dialog.Cancel();
                else if (gamepad.WasPressed(GamepadInput.A)) _nav.Activate();
                else _nav.Move(MenuInput.Horizontal(gamepad), MenuInput.Vertical(gamepad));
                return;
            }

            if (gamepad.WasPressed(GamepadInput.B) || gamepad.WasPressed(GamepadInput.Start))
            {
                Close();
                return;
            }

            _nav.Move(0, MenuInput.Vertical(gamepad));

            // Left and right step whatever is selected (the selectors of the training options), buttons have no use for them
            int horizontal = MenuInput.Horizontal(gamepad);
            if (horizontal != 0) _nav.Adjust(horizontal);

            if (gamepad.WasPressed(GamepadInput.A))
            {
                _nav.Activate();
                return;
            }
        }
    }

    /// <summary>
    /// Helper method to run the countdown. It starts the moment nobody has the fight paused any more,
    /// and starts over from the top if somebody pauses again before it is done.
    /// </summary>
    private void UpdateCountdown(float dt)
    {
        bool paused = IsShowing;

        if (_wasPaused && !paused) StartCountdown();
        _wasPaused = paused;

        if (paused || _countdown <= 0.0f) return;

        _countdown = MathF.Max(0.0f, _countdown - dt);

        // Every number lands with a thump
        int count = (int)MathF.Ceiling(_countdown);
        if (count == _shownCount || count <= 0) return;

        _shownCount = count;
        _count.Text = count.ToString();
        _count.Punch(0.4f, 0.3f);
    }

    private static bool StartPressed()
    {
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (gamepad.IsConnected && gamepad.WasPressed(GamepadInput.Start)) return true;
        }

        return false;
    }

    public override void Capture()
    {
        _shown.Publish(HoldsFight);
        _compositor.Capture();
    }

    public override void Render(float dt)
    {
        RenderFrame frame = RenderFrame.Active;
        bool shown = frame.IsDecoupled ? _shown.TryGet(frame, out bool up) && up : HoldsFight;

        if (shown) _compositor.Render(dt);
    }
}
