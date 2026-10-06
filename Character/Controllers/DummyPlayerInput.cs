using System;
using System.Numerics;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Input for the AI opponent. It walks up to the player (jumping whatever is in the way), throws kicks and punches, guards
/// what it sees coming, hits back after a guard and follows up on somebody it has stunned.
/// It plays by pressing the same buttons a gamepad would, and so it is bound by the same move list, buffering and interrupt rules as a real player.
/// </summary>
internal class DummyPlayerInput : IPlayerInput
{
    private const float WALK_SPEED_SCALE = 0.45f;   // How fast it walks compared to a player
    private const float REACTION_TIME = 0.3f;       // How long the other player has to be in range before it attacks
    private const float MIN_ATTACK_DELAY = 0.6f;    // Shortest pause between two attacks nobody gave it an opening for
    private const float MAX_ATTACK_DELAY = 1.5f;    // Longest pause between two of them
    private const float PUNCH_CHANCE = 0.35f;       // How often such an attack is a punch rather than a kick

    private const float BLOCK_CHANCE = 0.55f;       // How often it sees an attack coming and guards
    private const float BLOCK_LINGER = 0.12f;       // How long the guard stays up after there is nothing left to guard against
    private const float MAX_BLOCK_TIME = 1.2f;      // How long it keeps a guard up at the most, whatever the other player is up to
    private const float PUNISH_CHANCE = 0.7f;       // How often it hits back at somebody who has just missed it or hit its guard
    private const float FOLLOW_UP_CHANCE = 0.75f;   // How often it goes on hitting somebody it has stunned
    private const int MAX_FOLLOW_UPS = 3;           // How many blows it lands on somebody in one stun before it lets them be
    private const float JUMP_CHANCE = 0.5f;         // How often it jumps over a dodge roll coming its way
    private const float AIR_ATTACK_CHANCE = 0.6f;   // How often it kicks on the way past somebody it is in the air next to

    private const float ATTACK_RANGE = 56f;         // How close it walks up, kicks reach slightly further
    private const float ROLL_DANGER_RANGE = 220f;   // How close a dodge roll has to start for it to bother jumping
    private const float TAP_DURATION = 0.05f;       // How long a button is held down for a single press
    private const float TAP_GAP = 0.04f;            // How long a button is let go of before it is pressed again, or it wouldn't count as one
    private const float WALK_PAUSE = 0.35f;         // Wait before walking again, any sooner and it reads as a double tap (roll)

    private const float STUCK_TIME = 0.2f;          // How long it walks without getting anywhere before it tries jumping
    private const float STUCK_DISTANCE = 4f;        // How little it has to have moved in that time to count as stuck
    private const float JUMP_HOLD = 0.08f;          // How long up is held for a jump
    private const float JUMP_COOLDOWN = 0.5f;       // Wait between two jumps, long enough to have landed from the first
    private const float CLIMB_HEIGHT = 40f;         // How far above it the other player has to be for it to jump after them
    private const float CLIMB_RANGE = 140f;         // And how close to them it has to be for that

    private PlayerController _controller = null!;
    private float _attackTimer = MAX_ATTACK_DELAY;
    private float _blockTimer, _blockTime, _pressTimer, _pressGap, _jumpTimer, _jumpCooldown, _walkPauseTimer;
    private InputFlags _pressed = InputFlags.A;
    private bool _isWalking, _sawRoll, _turnToGuard;

    // What the opening it last saw was, so each one is only decided on once
    private bool _sawOpening, _sawStun, _airAttacked;
    private int _followUps;

    // Where it was when it last checked whether it is getting anywhere, and how long ago that was
    private float _stuckX, _stuckTimer;

    public string Name => "Dummy";
    public float WalkSpeedScale => WALK_SPEED_SCALE;

    private Player Player => _controller.Player;
    private Player Opponent => _controller.Opponent;

    public void Attach(PlayerController controller)
    {
        _controller = controller;
    }

    public InputFlags Read()
    {
        const float dt = 1.0f / PlayerConfig.TICK_RATE;

        _attackTimer -= dt;
        _blockTimer -= dt;
        _pressTimer -= dt;
        _jumpTimer -= dt;
        _jumpCooldown -= dt;
        _walkPauseTimer -= dt;
        if (_pressTimer <= 0) _pressGap -= dt;

        Vector2 toOpponent = Opponent.Transform.Position - Player.Transform.Position;
        float distance = MathF.Abs(toOpponent.X);
        bool inRange = distance <= ATTACK_RANGE && MathF.Abs(toOpponent.Y) <= ATTACK_RANGE;
        bool grounded = _controller.StateTracker.IsGrounded;

        var them = Opponent.Controller;
        bool theyAttack = them.StateTracker.CurrentStatus == PlayerStatusType.Attacking;

        WatchForDodgeRoll(toOpponent);

        // Stunned: nothing it presses counts, and letting go of everything makes the first thing after a fresh press
        if (!_controller.IsInControl)
        {
            StopWalking();
            _blockTimer = 0;
            return InputFlags.None;
        }

        InputFlags direction = toOpponent.X < 0 ? InputFlags.DPadLeft : InputFlags.DPadRight;

        if (_blockTimer > 0)
        {
            // A guard only covers the side it is held up to and can't be turned round, so that comes first
            if (_turnToGuard)
            {
                _turnToGuard = false;
                _walkPauseTimer = WALK_PAUSE;
                return direction;
            }

            _blockTime += dt;

            // Kept up for as long as their blow is still on its way, and dropped the moment it has been thrown
            if (them.IsCommitted && _blockTime < MAX_BLOCK_TIME) _blockTimer = MathF.Max(_blockTimer, BLOCK_LINGER);
            else if (theyAttack && them.HasStruck) _blockTimer = 0;

            if (_blockTimer > 0)
            {
                StopWalking();
                return InputFlags.RightBumper;
            }
        }

        InputFlags input = InputFlags.None;

        // Walk up to the other player, also turn around when they have gotten behind us (rolled past etc.)
        bool facingAway = !Player.IsFacing(Opponent);
        bool walking = (distance > ATTACK_RANGE || facingAway) && _walkPauseTimer <= 0;
        if (walking)
        {
            input |= direction;
            _isWalking = true;
        }
        else
        {
            StopWalking();
        }

        if (grounded)
        {
            _airAttacked = false;
            input |= FindWayUp(walking, toOpponent, dt);
        }
        else
        {
            _stuckTimer = 0;
        }

        LookForOpenings(inRange, facingAway, grounded, toOpponent);

        if (!inRange)
        {
            // The reaction time starts over whenever they are out of range, so the player always gets the first go
            _attackTimer = MathF.Max(_attackTimer, REACTION_TIME);
        }
        else if (_attackTimer <= 0 && !facingAway && them.StateTracker.CurrentStatus != PlayerStatusType.Invulnerable)
        {
            // Press one of the attacks, then leave a gap for the player to hit back
            Press(Random.Shared.NextSingle() < PUNCH_CHANCE ? Punch() : Kick());
            _attackTimer = MIN_ATTACK_DELAY + Random.Shared.NextSingle() * (MAX_ATTACK_DELAY - MIN_ATTACK_DELAY);
        }

        if (_pressTimer > 0) input |= _pressed;
        if (_jumpTimer > 0) input |= InputFlags.DPadUp;

        return input;
    }

    /// <summary>
    /// Helper method to get over what is in the way: a step it has walked into, or the ledge the other player is standing on.
    /// </summary>
    /// <returns>The direction to keep holding through the jump, so it carries over whatever it is.</returns>
    private InputFlags FindWayUp(bool walking, Vector2 toOpponent, float dt)
    {
        InputFlags towards = toOpponent.X < 0 ? InputFlags.DPadLeft : InputFlags.DPadRight;

        // Walking and not getting anywhere means there is something in the way, only a move that can be walked in counts
        if (walking && _controller.CanSteer)
        {
            _stuckTimer += dt;
            if (_stuckTimer >= STUCK_TIME)
            {
                bool stuck = MathF.Abs(Player.Transform.Position.X - _stuckX) < STUCK_DISTANCE;
                _stuckX = Player.Transform.Position.X;
                _stuckTimer = 0;

                if (stuck) Jump();
            }
        }
        else
        {
            _stuckX = Player.Transform.Position.X;
            _stuckTimer = 0;
        }

        // They are up on something right next to us, walking underneath them gets us nowhere
        if (toOpponent.Y > CLIMB_HEIGHT && MathF.Abs(toOpponent.X) < CLIMB_RANGE && Opponent.Controller.StateTracker.IsGrounded)
        {
            Jump();
            return towards;
        }

        return InputFlags.None;
    }

    private void Jump()
    {
        if (_jumpCooldown > 0) return;

        _jumpTimer = JUMP_HOLD;
        _jumpCooldown = JUMP_COOLDOWN;
    }

    /// <summary>
    /// Helper method for the attacks it doesn't wait its turn for: the ones the other player leaves themselves open to.
    /// Each opening is decided on once, when it comes up.
    /// </summary>
    private void LookForOpenings(bool inRange, bool facingAway, bool grounded, Vector2 toOpponent)
    {
        var them = Opponent.Controller;
        bool usable = inRange && !facingAway;

        // Somebody it has stunned can be hit again before they get out of it, a few times
        bool stunned = them.StateTracker.IsStunned;
        if (!stunned) _followUps = 0;
        if (stunned && !_sawStun) _followUps = Random.Shared.NextSingle() < FOLLOW_UP_CHANCE ? MAX_FOLLOW_UPS : 0;
        _sawStun = stunned;

        if (stunned && usable && _followUps > 0 && _controller.CanStartMove && CanPress)
        {
            // Whatever is quickest, there is no telling how long they have left
            Press(Punch());
            _followUps--;
            return;
        }

        // Somebody whose blow has been thrown and didn't stop us is wide open until their move is over
        bool open = them.StateTracker.CurrentStatus == PlayerStatusType.Attacking && them.HasStruck;
        if (open && !_sawOpening && usable && CanPress && Random.Shared.NextSingle() < PUNISH_CHANCE)
        {
            Press(Punch());
            _attackTimer = MathF.Max(_attackTimer, MIN_ATTACK_DELAY);
        }
        _sawOpening = open;

        // In the air next to them: a kick on the way past
        if (!grounded && !_airAttacked && toOpponent.Length() <= ATTACK_RANGE * 1.3f && !facingAway)
        {
            _airAttacked = true;
            if (Random.Shared.NextSingle() < AIR_ATTACK_CHANCE) Press(Kick());
        }
    }

    // Whether a button can be pressed right now and count as a new press
    private bool CanPress => _pressTimer <= 0 && _pressGap <= 0;

    private void Press(InputFlags button)
    {
        _pressed = button;
        _pressTimer = TAP_DURATION;
        _pressGap = TAP_GAP;
    }

    private static InputFlags Kick() => Random.Shared.NextSingle() < 0.5f ? InputFlags.A : InputFlags.B;
    private static InputFlags Punch() => Random.Shared.NextSingle() < 0.5f ? InputFlags.X : InputFlags.Y;

    /// <summary>
    /// Helper method to decide (once for every roll) if a dodge roll coming our way is jumped over.
    /// </summary>
    private void WatchForDodgeRoll(Vector2 toOpponent)
    {
        // Whatever the roll is called in the move list of the day, it is the move nothing can hit
        bool isRolling = Opponent.Controller.StateTracker.CurrentStatus == PlayerStatusType.Invulnerable;
        bool justStarted = isRolling && !_sawRoll;
        _sawRoll = isRolling;

        if (!justStarted || MathF.Abs(toOpponent.X) > ROLL_DANGER_RANGE) return;

        // Test if they are rolling towards us (they are on our right and facing left, or the other way around)
        bool towardsUs = Opponent.Flipped == (toOpponent.X > 0);
        if (towardsUs && Random.Shared.NextSingle() < JUMP_CHANCE)
        {
            _jumpTimer = JUMP_HOLD;
        }
    }

    private void StopWalking()
    {
        if (!_isWalking) return;

        _isWalking = false;
        _walkPauseTimer = WALK_PAUSE;
    }

    public void OnOpponentAttack()
    {
        // Nothing to guard with while it is stunned or in the middle of something of its own
        if (!_controller.IsInControl || !_controller.CanStartMove || !_controller.StateTracker.IsGrounded) return;

        // Kicks land too quickly to think about, so just like a player the dummy either sees it coming or doesn't
        Vector2 toOpponent = Opponent.Transform.Position - Player.Transform.Position;
        if (MathF.Abs(toOpponent.X) > ATTACK_RANGE * 2 || MathF.Abs(toOpponent.Y) > ATTACK_RANGE * 2) return;
        if (Random.Shared.NextSingle() >= BLOCK_CHANCE) return;

        _blockTimer = BLOCK_LINGER;
        _blockTime = 0;

        // With its back to them the guard would be up to the wrong side
        _turnToGuard = !Player.IsFacing(Opponent);
    }
}
