using System;

namespace Fighter2D.Fighters.Inputs.Dummy;

/// <summary>
/// The legs of the dummy. Walks up to the other player, jumps whatever is in the way, rolls in from far away and hops over rolls coming at it.
/// It has no idea what the map looks like, all it knows is whether it is getting anywhere.
/// </summary>
internal sealed class DummyFootwork(PlayerController controller, DummyHands hands)
{
    // A roll is a double tap. So let go, tap, let go, and then walking on is the second tap. One entry per tick
    private static readonly bool[] RollTaps = [false, false, false, true, true, true, false, false, false];

    private float _walkPause, _stuckTimer, _stuckX, _rollCooldown;
    private float _rollThink = DummyConfig.ROLL_THINK_TIME;
    private bool _walking, _sawRoll;

    // Where in the taps of a roll we are, -1 when we aren't rolling in
    private int _rollTap = -1;

    public void Tick()
    {
        _walkPause -= DummyConfig.TICK;
        _rollCooldown -= DummyConfig.TICK;
    }

    /// <summary>
    /// Lets go of the direction. It then waits a moment before walking again so the two presses don't read as a double tap.
    /// </summary>
    public void Stop()
    {
        if (!_walking) return;

        _walking = false;
        _walkPause = DummyConfig.WALK_PAUSE;
    }

    /// <summary>
    /// Called after something else tapped a direction, which needs the same pause as having walked.
    /// </summary>
    public void PauseAfterTap()
    {
        _walking = false;
        _walkPause = DummyConfig.WALK_PAUSE;
    }

    /// <summary>
    /// Called every tick the dummy is free to move. Returns the direction buttons to hold.
    /// </summary>
    public InputFlags Update(in DummyView view)
    {
        if (_rollTap >= 0) return ContinueRoll(view);

        // Walk up to the other player, also turn around when they have gotten behind us (rolled past etc.)
        bool walking = (view.Distance > DummyConfig.ATTACK_RANGE || view.FacingAway) && _walkPause <= 0;
        if (walking) _walking = true;
        else Stop();

        if (!view.Grounded)
        {
            _stuckTimer = 0;
            return walking ? view.Towards : InputFlags.None;
        }

        if (walking && ShouldRollIn(view))
        {
            _rollTap = 0;
            _rollCooldown = DummyConfig.ROLL_COOLDOWN;
            return ContinueRoll(view);
        }

        JumpIfStuck(walking);

        // They are up on something right next to us, walking around underneath them gets us nowhere
        bool climb = view.ToOpponent.Y > DummyConfig.CLIMB_HEIGHT && view.Distance < DummyConfig.CLIMB_RANGE && view.Them.State.IsGrounded;
        if (climb) hands.Jump();

        return walking || climb ? view.Towards : InputFlags.None;
    }

    /// <summary>
    /// Helper method to decide (once per roll) whether a dodge roll coming our way gets jumped over.
    /// </summary>
    public void WatchForDodgeRoll(in DummyView view)
    {
        // Whatever the roll is called in the move list of the day, it is the move nothing can hit
        bool rolling = view.Them.State.CurrentStatus == FighterStatus.Invulnerable;
        bool justStarted = rolling && !_sawRoll;
        _sawRoll = rolling;

        if (!justStarted || view.Distance > DummyConfig.ROLL_DANGER_RANGE) return;

        // Test if they are rolling towards us (they are on our right and facing left, or the other way around)
        bool towardsUs = view.Them.Player.Flipped == (view.ToOpponent.X > 0);
        if (towardsUs && Random.Shared.NextSingle() < DummyConfig.DODGE_JUMP_CHANCE) hands.JumpNow();
    }

    /// <summary>
    /// Helper method to decide whether to close a big gap with a roll instead of walking all the way like a mug.
    /// </summary>
    private bool ShouldRollIn(in DummyView view)
    {
        if (view.Distance <= DummyConfig.ROLL_IN_RANGE || !controller.CanSteer)
        {
            _rollThink = DummyConfig.ROLL_THINK_TIME;
            return false;
        }

        _rollThink -= DummyConfig.TICK;
        if (_rollThink > 0) return false;

        _rollThink = DummyConfig.ROLL_THINK_TIME;
        return _rollCooldown <= 0 && Random.Shared.NextSingle() < DummyConfig.ROLL_IN_CHANCE;
    }

    private InputFlags ContinueRoll(in DummyView view)
    {
        bool pressed = RollTaps[_rollTap++];

        if (_rollTap == RollTaps.Length)
        {
            // Done tapping. Walking on from the next tick is the second tap, so nothing is allowed to hold that up
            _rollTap = -1;
            _walkPause = 0;
            _stuckTimer = 0;
            _stuckX = controller.Player.Transform.Position.X;
        }

        return pressed ? view.Towards : InputFlags.None;
    }

    /// <summary>
    /// Helper method to hop when we are walking but not getting anywhere, which means there is a step in the way.
    /// </summary>
    private void JumpIfStuck(bool walking)
    {
        float x = controller.Player.Transform.Position.X;

        // Only a move that can be walked in counts, standing still in an attack isn't being stuck
        if (!walking || !controller.CanSteer)
        {
            _stuckX = x;
            _stuckTimer = 0;
            return;
        }

        _stuckTimer += DummyConfig.TICK;
        if (_stuckTimer < DummyConfig.STUCK_TIME) return;

        bool stuck = MathF.Abs(x - _stuckX) < DummyConfig.STUCK_DISTANCE;
        _stuckX = x;
        _stuckTimer = 0;

        if (stuck) hands.Jump();
    }
}
