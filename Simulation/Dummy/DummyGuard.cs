using System;

namespace Fighter2D.Fighters.Inputs.Dummy;

/// <summary>
/// The defensive half of the dummy's brain. It blocks attacks it sees coming and drops the block the moment the hit has been thrown.
/// </summary>
internal sealed class DummyGuard(PlayerController controller)
{
    // How much longer the block stays up, and how long it has been up for
    private float _timer, _time;

    // Blocking only covers the front and can't be turned around, so with our back to them we turn first
    private bool _turnFirst;

    // Whether what is coming is a low, which has to be blocked crouching
    private bool _low;

    /// <summary>
    /// Whether the last thing <see cref="Update"/> returned was the tap that turns us around, which counts as a press of a direction.
    /// </summary>
    public bool JustTurned { get; private set; }

    /// <summary>
    /// Called the moment the other player starts an attack. Attacks come out too fast to think about, so it either sees it coming or it doesn't.
    /// </summary>
    public void OnOpponentAttack()
    {
        // Can't block while in hitstun, in the air or in the middle of something else
        if (!controller.IsInControl || !controller.CanStartMove || !controller.State.IsGrounded) return;

        var toOpponent = controller.Opponent.Transform.Position - controller.Player.Transform.Position;
        if (MathF.Abs(toOpponent.X) > DummyConfig.BLOCK_RANGE || MathF.Abs(toOpponent.Y) > DummyConfig.BLOCK_RANGE) return;
        if (Random.Shared.NextSingle() >= DummyConfig.BLOCK_CHANCE) return;

        _timer = DummyConfig.BLOCK_LINGER;
        _time = 0;
        _turnFirst = !controller.Player.IsFacing(controller.Opponent);

        // It reads the move the way a player reads the animation, a sweep gets a crouching block
        _low = controller.Opponent.Controller.CurrentMove.Level == HitLevel.Low;
    }

    public void Drop() => _timer = 0;

    /// <summary>
    /// Called every tick. Returns the buttons to hold if the dummy is busy blocking, null if it has better things to do.
    /// </summary>
    public InputFlags? Update(in DummyView view)
    {
        JustTurned = false;

        _timer -= DummyConfig.TICK;
        if (_timer <= 0) return null;

        if (_turnFirst)
        {
            _turnFirst = false;
            JustTurned = true;
            return view.Towards;
        }

        _time += DummyConfig.TICK;

        // Keep it up for as long as their hit is still on its way, and drop it the moment it has been thrown
        if (view.Them.IsInStartup && _time < DummyConfig.MAX_BLOCK_TIME) _timer = MathF.Max(_timer, DummyConfig.BLOCK_LINGER);
        else if (view.Them.IsRecovering) _timer = 0;

        if (_timer <= 0) return null;

        return InputFlags.RightBumper | (_low ? InputFlags.DPadDown : InputFlags.None);
    }
}
