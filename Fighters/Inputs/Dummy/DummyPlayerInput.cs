namespace Fighter2D.Fighters.Inputs.Dummy;

/// <summary>
/// Input for the AI opponent. It walks (or rolls) up to the player, throws kicks and punches, blocks what it sees coming and punishes whiffs.
/// It plays by pressing the same buttons a gamepad would, so it is bound by the same move list, buffering and cancel rules as a real player.
/// The thinking is split up into its legs (<see cref="DummyFootwork"/>), its block (<see cref="DummyGuard"/>) and its attacks (<see cref="DummyOffence"/>).
/// Training mode can tell it to stop thinking and just stand there, crouch or hold a block (<see cref="Mode"/>).
/// </summary>
internal class DummyPlayerInput : IPlayerInput
{
    private PlayerController _controller = null!;
    private DummyHands _hands = null!;
    private DummyFootwork _footwork = null!;
    private DummyGuard _guard = null!;
    private DummyOffence _offence = null!;

    /// <summary>
    /// What the dummy does with itself. Fighting unless training mode says otherwise.
    /// </summary>
    public DummyMode Mode { get; set; } = DummyMode.Fight;

    /// <summary>
    /// The behaviour written in HIDL it follows while its mode is <see cref="DummyMode.Script"/>, see <see cref="DummyScript"/>.
    /// </summary>
    public DummyScript? Script { get; set; }

    public float WalkSpeedScale => DummyConfig.WALK_SPEED_SCALE;

    public void Attach(PlayerController controller)
    {
        _controller = controller;
        _hands = new DummyHands();
        _footwork = new DummyFootwork(controller, _hands);
        _guard = new DummyGuard(controller);
        _offence = new DummyOffence(controller, _hands);
    }

    public void OnOpponentAttack()
    {
        if (Mode == DummyMode.Fight) _guard.OnOpponentAttack();
    }

    public InputFlags Read()
    {
        var view = new DummyView(_controller);

        _hands.Tick();
        _footwork.Tick();

        if (Mode == DummyMode.Script) return RunScript(view);
        if (Mode != DummyMode.Fight) return Pose(view);

        _footwork.WatchForDodgeRoll(view);

        // In hitstun nothing it presses counts, and letting go of everything makes the first thing after a fresh press
        if (!_controller.IsInControl)
        {
            _footwork.Stop();
            _guard.Drop();
            return InputFlags.None;
        }

        // Blocking beats everything else, a wrong guess costs the dummy its turn
        if (_guard.Update(view) is { } blocking)
        {
            if (_guard.JustTurned) _footwork.PauseAfterTap();
            else _footwork.Stop();

            return blocking;
        }

        InputFlags direction = _footwork.Update(view);
        _offence.Update(view);

        return direction | _hands.Held;
    }

    /// <summary>
    /// Helper method for a dummy that is being told what to do by a script. Its hands and feet are the script's.
    /// </summary>
    private InputFlags RunScript(in DummyView view)
    {
        _footwork.Stop();
        _guard.Drop();

        return Script?.Read(_controller, view) ?? InputFlags.None;
    }

    /// <summary>
    /// Helper method for the training dummy, which holds one pose and lets the player work on it.
    /// </summary>
    private InputFlags Pose(in DummyView view)
    {
        _footwork.Stop();
        _guard.Drop();

        if (!_controller.IsInControl) return InputFlags.None;

        switch (Mode)
        {
            case DummyMode.Crouch:
                return InputFlags.DPadDown;

            case DummyMode.Block:
            case DummyMode.CrouchBlock:
                // A block only covers the front and can't be turned around, so it faces the player first
                if (view.FacingAway && !_controller.IsBlocking) return view.Towards;

                return InputFlags.RightBumper | (Mode == DummyMode.CrouchBlock ? InputFlags.DPadDown : InputFlags.None);

            default:
                return InputFlags.None;
        }
    }
}
