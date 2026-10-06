using Fighter2D.Logic;

namespace Fighter2D.Character.Controllers.Dummy;

/// <summary>
/// Input for the AI opponent. It walks (or rolls) up to the player, throws kicks and punches, blocks what it sees coming and punishes whiffs.
/// It plays by pressing the same buttons a gamepad would, so it is bound by the same move list, buffering and cancel rules as a real player.
/// The thinking is split up into its legs (<see cref="DummyFootwork"/>), its block (<see cref="DummyGuard"/>) and its attacks (<see cref="DummyOffence"/>).
/// </summary>
internal class DummyPlayerInput : IPlayerInput
{
    private PlayerController _controller = null!;
    private DummyHands _hands = null!;
    private DummyFootwork _footwork = null!;
    private DummyGuard _guard = null!;
    private DummyOffence _offence = null!;

    public string Name => "Dummy";
    public float WalkSpeedScale => DummyConfig.WALK_SPEED_SCALE;

    public void Attach(PlayerController controller)
    {
        _controller = controller;
        _hands = new DummyHands();
        _footwork = new DummyFootwork(controller, _hands);
        _guard = new DummyGuard(controller);
        _offence = new DummyOffence(controller, _hands);
    }

    public void OnOpponentAttack() => _guard.OnOpponentAttack();

    public InputFlags Read()
    {
        var view = new DummyView(_controller);

        _hands.Tick();
        _footwork.Tick();
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
}
