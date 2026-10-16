using System;
using System.Numerics;

namespace Fighter2D.Simulation;

// The controller being hit and hitting. Throwing the hit on its frame and keeping it out over the active frames, the
// stuns, the hitlag both ways with the launch at the end of it (DI on the way out, SDI during), stale moves, and the
// tech on the way down from a knockdown with the parry's note of when the block went up. What a hit comes to is the
// HitResolver's, this is what it does to a fighter.
internal sealed partial class PlayerController
{
    /* Hitting and getting hit, what a hit comes to is up to the HitResolver */

    /// <summary>
    /// Called by the move exactly on the frame its hit comes out.
    /// </summary>
    /// <param name="frame">The frame of the animation the hit comes out on, which is what its hitbox is read from.</param>
    public void ThrowHit(uint frame)
    {
        HitThrown = true;
        int active = Math.Max(1, CurrentMove.ActiveFrames);
        ActiveFrame = 0;
        HitLanded = HitResolver.TryLand(this, ClampFrame(frame), active <= 1);
        _activeLeft = HitLanded ? 0 : active - 1;
        _whiffed = !HitLanded && active <= 1;
    }

    /// <summary>
    /// Called by the move on every frame after its hit frame. The hitbox is still out on its active frames and gets
    /// another go at landing on every one of them, a whiff is only a whiff on the last.
    /// </summary>
    public void KeepHitting(uint frame)
    {
        if (ActiveFrame >= 0) ActiveFrame = ActiveFrame + 1 < Math.Max(1, CurrentMove.ActiveFrames) ? ActiveFrame + 1 : -1;
        if (HitThrown && ActiveFrame < 0) _recoveryFrames++;
        if (_activeLeft <= 0) return;

        _activeLeft--;
        if (HitResolver.TryLand(this, ClampFrame(frame), _activeLeft == 0))
        {
            HitLanded = true;
            _activeLeft = 0;
        }
        else if (_activeLeft == 0) _whiffed = true;
    }

    /// <summary>
    /// Called by the moves of the other player the moment they start an attack.
    /// </summary>
    public void OnOpponentAttack() => Input.OnOpponentAttack();

    /// <summary>
    /// Takes the controls away for a number of ticks. Whatever we were doing ends here and we sit in the hitstun move until it is over.
    /// </summary>
    /// <param name="damage">How much the hit that did it took off us, for the combo counter.</param>
    /// <param name="knockdown">Whether the hit puts us on the floor once we come down from it, see <see cref="KnockDown"/>.</param>
    /// <param name="gravityShare">How much of our gravity we fall with until it is over.</param>
    /// <param name="juggle">Whether we were already up in the air in hitstun when it landed.</param>
    public void ApplyHitstun(int ticks, int damage = 0, bool knockdown = false, float gravityShare = 1.0f, bool juggle = false)
    {
        State.ApplyHitstun(ticks, damage, knockdown, gravityShare, juggle);
        Movement.UpdateGravity();

        // Every hit restarts the move, so each hit of a combo is seen to land
        ChangeToMove(MoveIds.HIT_STUN, forceRestart: true);
    }

    /// <summary>
    /// Keeps us in our block for a number of ticks after it ate a hit. No move starts until it is over, and the block can't be dropped.
    /// </summary>
    public void ApplyBlockstun(int ticks) => State.ApplyBlockstun(ticks);

    /// <summary>
    /// Puts us on the floor. Nothing hits us down here, and once the knockdown runs out we get up with i-frames (the get_up move's).
    /// Called by the hit that does it on the ground, and by the landing for one that launched us first.
    /// </summary>
    public void KnockDown()
    {
        State.KnockDown(CombatRules.KNOCKDOWN_TICKS);

        // A move list without a floor to lie on makes do with the hitstun move
        ChangeToMove(MoveList.Moves.ContainsKey(MoveIds.KNOCKED_DOWN) ? MoveIds.KNOCKED_DOWN : MoveIds.HIT_STUN, forceRestart: true);
    }

    /// <summary>
    /// Freezes the player on the frame they are on, and where they are, for a number of ticks. Whatever they were doing
    /// carries on once it is over, unless the hit queued a launch or a shove (QueueLaunch), which goes then.
    /// </summary>
    public void ApplyHitstop(int ticks)
    {
        if (ticks <= 0)
        {
            if (_hitstop == 0) ReleaseHitstop();
            return;
        }

        if (_hitstop == 0) _resumeVelocity = Body.Velocity;
        _hitstop = Math.Max(_hitstop, ticks);
    }

    /// <summary>
    /// What the body does once the hitlag is over, a launch or a shove. A launch (influenced) can be turned by the direction
    /// held as the hitlag ends, which is DI.
    /// </summary>
    public void QueueLaunch(Vector2 velocity, bool influenced)
    {
        _pendingLaunch = velocity;
        _pendingInfluence = influenced;
    }

    /// <summary>
    /// Helper method for the end of a hitlag, the body goes again, with the launch it was queued if there is one.
    /// </summary>
    private void ReleaseHitstop()
    {
        Vector2 velocity = _pendingLaunch ?? _resumeVelocity;
        if (_pendingLaunch is not null && _pendingInfluence && State.IsInHitstun) velocity = Influence(velocity);

        _pendingLaunch = null;
        _resumeVelocity = Vector2.Zero;
        _sdi = Vector2.Zero;
        Body.SetVelocity(velocity);
        Movement.UpdateGravity();
    }

    /// <summary>
    /// Helper method to turn a launch by the direction held, up to DI_MAX_ANGLE, most from a direction square on to it.
    /// </summary>
    private Vector2 Influence(Vector2 launch)
    {
        Vector2 held = HeldDirection();
        float speed = launch.Length();
        if (held == Vector2.Zero || speed < 1.0f) return launch;

        Vector2 across = new Vector2(-launch.Y, launch.X) / speed;
        float turn = Vector2.Dot(Vector2.Normalize(held), across) * CombatRules.DI_MAX_ANGLE;
        float cos = MathF.Cos(turn), sin = MathF.Sin(turn);
        return new Vector2(launch.X * cos - launch.Y * sin, launch.X * sin + launch.Y * cos);
    }

    /// <summary>
    /// Helper method for SDI, every direction pressed during a hitlag we took shoves us that way, one every few ticks.
    /// </summary>
    private void TakeSdi()
    {
        if (!State.IsInHitstun) return;
        if (_sdiCooldown > 0)
        {
            _sdiCooldown--;
            return;
        }

        InputFlags pressed = Inputs.Buffer.PressedNow;
        float x = ((pressed & InputFlags.DPadRight) != 0 ? 1 : 0) - ((pressed & InputFlags.DPadLeft) != 0 ? 1 : 0);
        float y = ((pressed & InputFlags.DPadUp) != 0 ? 1 : 0) - (!State.IsGrounded && (pressed & InputFlags.DPadDown) != 0 ? 1 : 0);
        if (x == 0 && y == 0) return;

        _sdi += Vector2.Normalize(new Vector2(x, y)) * CombatRules.SDI_DISTANCE;
        _sdiCooldown = CombatRules.SDI_INTERVAL;
    }

    /// <summary>
    /// How far the SDI of this tick shoves us, taken by the physics once.
    /// </summary>
    public Vector2 TakeSdiShove()
    {
        Vector2 shove = _sdi;
        _sdi = Vector2.Zero;
        return shove;
    }

    private Vector2 HeldDirection() => new(
        Inputs.Steering,
        (Inputs.IsHeld(InputFlags.DPadUp) ? 1 : 0) - (Inputs.IsHeld(InputFlags.DPadDown) ? 1 : 0));

    /// <summary>
    /// How much of its damage a move of ours does, less for every time it landed lately (Smash's stale move negation).
    /// </summary>
    public float StaleScale(FightingMove move)
    {
        float scale = 1.0f;
        int length = _stale.Length;
        for (int place = 0; place < length; place++)
        {
            if (_stale[(_staleNext - 1 - place + length) % length] == move.Id) scale -= CombatRules.STALE_STEPS[place];
        }
        return scale;
    }

    /// <summary>
    /// Remembers a move of ours that landed, for stale move negation.
    /// </summary>
    public void RememberLanded(FightingMove move)
    {
        _stale[_staleNext] = move.Id;
        _staleNext = (_staleNext + 1) % _stale.Length;
    }

    /* Teching */

    /// <summary>
    /// Helper method to note the tick our block goes up on (for parries) and a press of block on the way down from a
    /// knockdown (for teching).
    /// </summary>
    private void WatchBlockAndTech()
    {
        bool blocking = IsBlocking;
        if (blocking && !_wasBlocking) BlockStartedAt = Tick;
        _wasBlocking = blocking;

        if (!State.IsInHitstun || !State.KnockdownPending || State.IsGrounded) return;
        if ((Inputs.Buffer.PressedNow & InputFlags.RightBumper) == 0) return;

        // Mashing it gets you nothing, a press that wasn't in time locks it out for a while
        if (_techAt == 0 || Tick - _techAt > CombatRules.TECH_LOCKOUT) _techAt = Tick;
    }

    /// <summary>
    /// Called as we land from a knockdown. If block was pressed in time we get up on the spot, or roll the way we hold,
    /// instead of lying there.
    /// </summary>
    /// <returns>Whether we teched.</returns>
    public bool TryTech()
    {
        if (_techAt == 0 || Tick - _techAt > CombatRules.TECH_WINDOW) return false;
        _techAt = 0;

        State.Tech();

        float direction = Inputs.Steering;
        if (direction != 0 && MoveList.Moves.ContainsKey(MoveIds.TECH_ROLL))
        {
            // The roll goes the way it is facing, so face the way it was asked for
            Body.Flipped = direction < 0;
            ChangeToMove(MoveIds.TECH_ROLL, forceRestart: true);
        }
        else ChangeToMove(MoveList.Moves.ContainsKey(MoveIds.TECH) ? MoveIds.TECH : MoveIds.GET_UP, forceRestart: true);

        return true;
    }
}
