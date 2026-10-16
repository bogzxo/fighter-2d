using System;

using Horizon.Content;
using Horizon.Core.Components;
using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.HUD;

/// <summary>
/// The lab of a practice fight, a drawer that slides in from the left over the fight while it carries on. The back
/// button on a gamepad (backspace on the keys) brings it out and puts it away. While it is out the gamepad is the
/// drawer's, player one's fighter stands still (<see cref="IsOpen"/>, which the fight mutes player one's input by) and
/// the dummy goes on doing whatever it was told, so changing what it does is seen straight away. It used to be three
/// levels down in the pause menu with a countdown on the way back, which is no way to run a lab.
/// The layout is Assets/ui/layouts/training_drawer.hor.
/// </summary>
internal sealed class TrainingDrawer(TrainingMode training) : GameComponent
{
    private UICompositor _compositor = null!;
    private UILayout _layout = null!;
    private UINavigator _nav = null!;
    private UIComponent _drawer = null!;
    private Button _hitboxes = null!;

    private readonly MenuActions _actions = new();
    private HintBar _hint = null!;

    /// <summary>Whether the drawer is out. Player one's fighter is muted for as long as it is.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Asked when somebody presses back, false keeps the drawer shut (the versus screen, a pause that is up).</summary>
    public Func<bool>? CanOpen { get; init; }

    /// <summary>Whether something else has the gamepads right now, the pause menu. The drawer keeps its hands off them then.</summary>
    public Func<bool>? Held { get; init; }

    /// <summary>Draws the boxes of the fight or stops, and says which it is doing now.</summary>
    public Func<bool>? ToggleHitboxes { get; init; }

    /// <summary>Whether the boxes are being drawn, for what the button says before anybody has pressed it.</summary>
    public Func<bool>? HitboxesShown { get; init; }

    /// <summary>The frame bars at the bottom of the screen, up or not (the HUD's <c>FrameData</c>). Null leaves the row out.</summary>
    public Func<bool>? FrameDataShown { get; init; }
    public Action<bool>? ShowFrameData { get; init; }

    public override void Initialize()
    {
        (_layout, _compositor) = MenuLayouts.Load(MenuLayouts.TRAINING_DRAWER);
        _compositor.Initialize();

        _drawer = _layout.Get<UIComponent>("drawer");
        _hint = new HintBar(_layout.Get<Label>("hint"), _actions);
        _nav = _layout.Module.Navigation;

        WireTraining();

        // Back or B puts it away, the d-pad walks it and steps the selectors, A presses what it is on
        _actions.Add(GamepadInput.B, "hints.close", _ => Close());
        _actions.Add(new MenuAction { Button = GamepadInput.Back, Shown = false, Pressed = _ => Close() });
        _actions.Directions("hints.choose_and_change", Walk);
        _actions.Add(GamepadInput.A, "hints.pick", _ => _nav.Activate());

        // Put away to begin with, without sliding out on the first frame
        _drawer.Show(false, instantly: true);
    }

    private void WireTraining()
    {
        // The built in modes and whatever behaviours the scripts of the content have to offer
        var dummy = _layout.Get<Selector>("dummy");
        dummy.Options = training.DummyChoices;
        dummy.Index = training.DummyChoice;
        dummy.OnChanged = _ => training.DummyChoice = dummy.Index;

        // Which character the dummy is. Picking another one builds the fight again around it, so the drawer goes with the fight
        var opponent = _layout.Get<Selector>("opponent");
        if (training.CanChangeOpponent)
        {
            opponent.Options = training.OpponentChoices;
            opponent.Index = training.OpponentChoice;
            opponent.OnChanged = _ => training.OpponentChoice = opponent.Index;
        }
        else _layout.Get<StackPanel>("opponent_row").Visible = false;

        var refill = _layout.Get<Selector>("refill");
        refill.Options = [Localisation.Get("common.off"), Localisation.Get("common.on")];
        refill.Index = training.RefillHealth ? 1 : 0;
        refill.OnChanged = _ => training.RefillHealth = refill.Index == 1;

        // Seen at once, the bars come up or go while the dummy carries on
        var frames = _layout.Get<Selector>("frame_data");
        if (FrameDataShown is { } shown && ShowFrameData is { } show)
        {
            frames.Options = [Localisation.Get("common.off"), Localisation.Get("common.on")];
            frames.Index = shown() ? 1 : 0;
            frames.OnChanged = _ => show(frames.Index == 1);
        }
        else _layout.Get<StackPanel>("frame_data_row").Visible = false;

        _hitboxes = _layout.Get<Button>("btn_hitboxes");
        if (ToggleHitboxes is { } toggle)
        {
            ShowHitboxes(HitboxesShown?.Invoke() == true);
            _hitboxes.OnPressed = () => ShowHitboxes(toggle());
        }
        else _hitboxes.Visible = false;

        _layout.Get<Button>("btn_reset").OnPressed = () =>
        {
            training.ResetPositions();
            Close();
        };
    }

    private void ShowHitboxes(bool shown) => _hitboxes.Label = Localisation.Get(shown ? "pause.hitboxes_on" : "pause.hitboxes_off");

    private void Open()
    {
        IsOpen = true;
        _drawer.Show(true);
        _nav.SelectFirst();
    }

    private void Close()
    {
        IsOpen = false;
        _drawer.Show(false);
        _nav.Select(null);
    }

    private void Walk(Gamepad gamepad, int right, int down)
    {
        _nav.Move(0, down);
        if (right != 0) _nav.Adjust(right);
    }

    public override void UpdateState(float dt)
    {
        bool held = Held?.Invoke() == true;

        // A pause on top of an open drawer puts the drawer away, the pause has the screen
        if (held && IsOpen) Close();

        if (!held)
        {
            if (!IsOpen)
            {
                if (BackPressed() && CanOpen?.Invoke() != false) Open();
            }
            else
            {
                _hint.Update(dt);
                _actions.Update();
            }
        }

        _compositor.UpdateState(dt);
    }

    private static bool BackPressed()
    {
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (gamepad.IsConnected && gamepad.WasPressed(GamepadInput.Back)) return true;
        }

        return false;
    }

    public override void Capture() => _compositor.Capture();

    // Drawn every frame, sliding out is drawing too, and a drawer that is all the way in is nothing to draw
    public override void Render(float dt) => _compositor.Render(dt);
}
