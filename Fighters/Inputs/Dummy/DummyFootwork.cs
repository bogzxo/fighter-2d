using System;

namespace Fighter2D.Fighters.Inputs.Dummy;

/// <summary>
/// The legs of the dummy. Walks up to the other player, jumps whatever is in the way, rolls in from far away and hops over rolls coming at it.
/// It knows what the map looks like (see <see cref="StageRoute"/>). The way to the player is found on its tiles, so a step, a gap
/// or a ledge is jumped from the right tile at the first try and the landing is aimed in the air. On a map where no way can be
/// found it is back to noticing that it isn't getting anywhere and jumping at whatever that is.
/// </summary>
internal sealed class DummyFootwork(PlayerController controller, DummyHands hands)
{
    // A roll is a double tap. So let go, tap, let go, and then walking on is the second tap. One entry per tick
    private static readonly bool[] RollTaps = [false, false, false, true, true, true, false, false, false];

    private readonly StageRoute _route = new(controller);
    private float _routeAge = DummyConfig.ROUTE_TIME;

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
        _route.Forget();
        _routeAge = DummyConfig.ROUTE_TIME;
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
        bool walking = (!view.InRange || view.FacingAway) && _walkPause <= 0;
        if (!walking)
        {
            Stop();
            JumpIfStuck(false);
            return InputFlags.None;
        }

        _walking = true;

        // With a way to go by, the way says which way to walk and when to jump. It also aims the landing, so in the air it is
        // the only thing listened to
        FollowRoute(view);
        if (_route.Found)
        {
            if (_route.ShouldJump && controller.CanSteer) hands.Jump();

            // Still, a tile can be wrong about what a fighter fits past. Not getting anywhere gets a jump as it always did
            JumpIfStuck(true);
            return Direction(_route.Steering);
        }

        if (!view.Grounded)
        {
            _stuckTimer = 0;
            return view.Towards;
        }

        if (ShouldRollIn(view))
        {
            _rollTap = 0;
            _rollCooldown = DummyConfig.ROLL_COOLDOWN;
            return ContinueRoll(view);
        }

        JumpIfStuck(true);

        // They are up on something right next to us, walking around underneath them gets us nowhere
        bool climb = view.ToOpponent.Y > DummyConfig.CLIMB_HEIGHT && view.Distance < DummyConfig.CLIMB_RANGE && view.Them.State.IsGrounded;
        if (climb) hands.Jump();

        return view.Towards;
    }

    /// <summary>
    /// Helper method to keep the way to the other player fresh. They don't stand still while we walk, so it is worked out again
    /// every so often, but only from the ground. In the air the way stays what it was when we took off
    /// </summary>
    private void FollowRoute(in DummyView view)
    {
        _routeAge += DummyConfig.TICK;

        if (view.Grounded && _routeAge >= DummyConfig.ROUTE_TIME)
        {
            _routeAge = 0.0f;
            _route.TryFind(controller.Opponent.FeetPosition, DummyConfig.WALK_SPEED_SCALE);
        }

        _route.Update();
    }

    private static InputFlags Direction(float steering) => steering switch
    {
        < 0.0f => InputFlags.DPadLeft,
        > 0.0f => InputFlags.DPadRight,
        _ => InputFlags.None
    };

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
        if (!walking || !controller.CanSteer || !controller.State.IsGrounded)
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
