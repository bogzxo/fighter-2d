using Horizon.Content;
using System;

using Horizon.Core.Components;
using Horizon.Core.Threading;
using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.HUD;

/// <summary>
/// The menu that comes up when somebody presses start in the middle of a fight. Carry on, start the match over or sod off to the main menu.
/// The training options of a practice fight are a drawer of their own that the fight goes on under, see <see cref="TrainingDrawer"/>.
/// This only is the menu. Holding the fight still while it is up is the job of the scene, which asks <see cref="HoldsFight"/>.
/// Online a pause is a pause for both machines. Whoever didn't press start gets told who did and waits for them to carry on,
/// only the one who paused can unpause (see <see cref="HeldByOther"/>).
/// Nobody is dropped straight back into the fight either. Once the pause is over (or the data has been reloaded) it counts
/// down from three first, and the fight stays held until it gets to the end of that.
/// The gamepad walks the menu through the engine's navigator, which is what lets it step the selectors as well as press the buttons.
/// It comes up on whatever it was left on the last time, so going back and forth between the fight and one of the
/// host's tools isn't a walk down the menu every time (see <see cref="lastSelected"/>).
/// </summary>
internal sealed class PauseMenu : GameComponent
{

    // What the menu was left on the last time it was up, by the name it has in the layout, null for the top of it. It
    // outlives the menu on purpose. A fight that is built again (another dummy, the data read again) has a
    // new menu, and that one comes up where the old one was. Leaving a fight forgets it, nobody wants the next fight's
    // pause to come up with the thumb already on Start over
    private static string? lastSelected;

    // The layout is three layers. The menu of whoever paused, what the other machine shows while it waits for them, and
    // the countdown both of them get before the fight is back on
    private const string MENU_LAYER = "menu";
    private const string WAITING_LAYER = "waiting";
    private const string COUNTDOWN_LAYER = "countdown";

    // What the countdown counts from, a second for each
    private const int COUNTDOWN_FROM = 3;

    private UICompositor _compositor = null!;
    private UILayout _layout = null!;
    private UINavigator _nav = null!;
    private Button _resume = null!;
    private Label _hint = null!;

    // What the buttons do while the menu is up, and the line that says so
    private readonly MenuActions _actions = new();
    private HintBar _hintBar = null!;
    private Readout _count = null!;

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
    /// Whether the menu keeps its hands off the gamepads, for while something is on top of the fight (the options).
    /// It stays up and the fight stays held, it just isn't the one being pressed.
    /// </summary>
    public bool Quiet { get; set; }
    
    /// <summary>
    /// Allows the players to open their options screen, useful for now just to test graphics options, change resolution etc.
    /// </summary>
    public required Action Options { get; init; }

    /// <summary>
    /// The tools of the host, a list of its own under the menu. Reading all the data again from disk and drawing the hitboxes.
    /// Null for whoever isn't the host (the machine that joined an online fight), who doesn't get the list at all.
    /// </summary>
    public Action? ReloadData { get; init; }
    public Func<bool>? ToggleHitboxes { get; init; }

    /// <summary>Whether the boxes are being drawn, for what the button says before anybody has pressed it.</summary>
    public Func<bool>? HitboxesShown { get; init; }

    public override void Initialize()
    {
        (_layout, _compositor) = MenuLayouts.Load(MenuLayouts.PAUSE_MENU);
        _compositor.Initialize();

        _resume = WireButton("btn_resume", Close);

        if (Rematch is { } rematch) WireButton("btn_rematch", () => Leave(rematch));
        else _layout.Get<Button>("btn_rematch").Visible = false;

        WireButton("btn_quit", AskToQuit);
        WireButton("btn_options", OpenOptions);
        WireHostTools();

        _hint = _layout.Get<Label>("hint");
        _hintBar = new HintBar(_hint, _actions);

        // B and start put the menu away. Before A, which is also start on a pad, so start closes rather than picks
        _actions.Add(GamepadInput.B, "hints.carry_on", _ => Close());
        _actions.Add(new MenuAction { Button = GamepadInput.Start, Shown = false, Pressed = _ => Close() });

        // Up and down walk the menu, left and right go across to the host's tools
        _actions.Directions("hints.choose", Walk);
        _actions.Add(GamepadInput.A, "hints.pick", _ => _nav.Activate());
        _count = _layout.Get<Readout>("count");

        // The gamepad walks whatever is on the menu layer, the hidden layers are skipped by themselves
        _nav = _layout.Module.Navigation;
        _nav.Changed = Remember;

        // None of it is up until somebody pauses
        _layout.Module.SetLayerVisible(MENU_LAYER, false);
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

            // Without the tools the panel they are in has nothing in it
            _layout.Get<StackPanel>("side").Visible = false;
            return;
        }

        // The menu stays up while the data is read again, the fight that comes back from it has no pause on
        WireButton("btn_reload", ReloadData);
        // It used to say off whatever the options had, the boxes on and the button saying they weren't
        hitboxes.Label = Localisation.Get(HitboxesShown?.Invoke() == true ? "pause.hitboxes_on" : "pause.hitboxes_off");
        WireButton("btn_hitboxes", () => hitboxes.Label = Localisation.Get(ToggleHitboxes() ? "pause.hitboxes_on" : "pause.hitboxes_off"));
    }

    /// <summary>
    /// Helper method to open the options menu up.
    /// </summary>
    private void OpenOptions()
    {
        Options();
    }
    /// <summary>
    /// Helper method to ask before the fight is thrown away. The question comes up over the menu and takes the gamepad with it.
    /// </summary>
    private void AskToQuit()
    {
        UIDialog.Show(_layout.Module, Localisation.Get("pause.leave_title"), Localisation.Get("pause.leave_question"),
            new DialogChoice(Localisation.Get("pause.leave_confirm"), () => Leave(Quit)),
            new DialogChoice(Localisation.Get("common.stay")));
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

        // The layers have to be up before the navigator will stand on anything in them. Back onto whatever it was left
        // on, if that is still there to stand on, and onto carrying on if it isn't
        ShowLayers();
        string? remembered = lastSelected;
        if (remembered is null || !_layout.TryGet(remembered, out UIComponent? last) || !_nav.Select(last)) _nav.Select(_resume);
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
        lastSelected = null;
        how();
    }

    /// <summary>
    /// Called by the navigator whenever it stands on something else. What it is on is kept by its name in the layout,
    /// the buttons of a question (which have none) and the menu being put away (which is nothing) aren't.
    /// </summary>
    private void Remember(UIComponent? selected)
    {
        if (!IsOpen || selected is null) return;

        foreach (var (name, part) in _layout.Parts)
        {
            if (!ReferenceEquals(part, selected)) continue;

            lastSelected = name;
            return;
        }
    }

    private void ShowLayers()
    {
        _layout.Module.SetLayerVisible(MENU_LAYER, IsOpen);
        _layout.Module.SetLayerVisible(WAITING_LAYER, !IsOpen && Waiting);
        _layout.Module.SetLayerVisible(COUNTDOWN_LAYER, !IsShowing && _countdown > 0.0f);
    }

    public override void UpdateState(float dt)
    {
        if (_leaving) return;

        UpdateCountdown(dt);

        // What the buttons do is no use to read while another screen has them, and it sat right under that one's own
        _hint.Visible = !Quiet;

        if (Quiet)
        {
            ShowLayers();
            _compositor.UpdateState(dt);
            return;
        }

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

        _hintBar.Update(dt);
        _compositor.UpdateState(dt);

        // A question that is up takes the gamepad, B is the answer that changes nothing. Whoever has a gamepad gets a
        // say otherwise, it is not just player one who needs a piss
        if (_layout.Module.Dialog is { } dialog)
        {
            MenuActions.DriveDialog(dialog, _nav);
            return;
        }

        _actions.Update();
    }

    private void Walk(Gamepad gamepad, int right, int down)
    {
        _nav.Move(0, down);
        if (right != 0 && !_nav.Adjust(right)) _nav.Move(right, 0);
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
        _count.Set(count);
        _count.Punch(0.4f, 0.3f);
    }

    private static bool StartPressed()
    {
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            // Escape on a keyboard (its B, which a fight has no other use for) as well, the way every game on a PC has it
            if (gamepad.WasPressed(GamepadInput.Start) || (gamepad.Kind == GamepadKind.Keyboard && gamepad.WasPressed(GamepadInput.B))) return true;
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
