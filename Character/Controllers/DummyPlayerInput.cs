using System;
using System.Numerics;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Input for a very basic AI opponent, it walks up to the player, kicks, maybe blocks, maybe jumps over a roll/
/// It plays by pressing the same buttons a gamepad would, and so it is bound by the same move list, buffering and interrupt rules as a real player.
/// </summary>
internal class DummyPlayerInput : IPlayerInput
{
    private const float WALK_SPEED_SCALE = 0.25f;   // How fast it walks compared to a player
    private const float REACTION_TIME = 0.4f;       // How long the other player has to be in range before it kicks
    private const float MIN_ATTACK_DELAY = 1.1f;    // Shortest pause between two kicks
    private const float MAX_ATTACK_DELAY = 2.2f;    // Longest pause between two kicks
    private const float BLOCK_CHANCE = 0.4f;        // How often it guesses a kick is coming and guards
    private const float BLOCK_DURATION = 0.4f;      // How long it keeps the guard up, it can't do anything else meanwhile
    private const float JUMP_CHANCE = 0.5f;         // How often it jumps over a dodge roll coming its way

    private const float ATTACK_RANGE = 56f;         // How close it walks up, kicks reach slightly further
    private const float ROLL_DANGER_RANGE = 220f;   // How close a dodge roll has to start for it to bother jumping
    private const float TAP_DURATION = 0.05f;       // How long a button is held down for a single press
    private const float WALK_PAUSE = 0.35f;         // Wait before walking again, any sooner and it reads as a double tap (roll)

    private PlayerController _controller = null!;
    private float _attackTimer = MAX_ATTACK_DELAY;
    private float _blockTimer, _kickTimer, _jumpTimer, _walkPauseTimer;
    private InputFlags _kickButton = InputFlags.A;
    private bool _isWalking, _sawRoll;

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
        const float dt = 1.0f / PlayerConfig.INPUT_TICK_RATE;

        _attackTimer -= dt;
        _blockTimer -= dt;
        _kickTimer -= dt;
        _jumpTimer -= dt;
        _walkPauseTimer -= dt;

        Vector2 toOpponent = Opponent.Transform.Position - Player.Transform.Position;
        float distance = MathF.Abs(toOpponent.X);
        bool inRange = distance <= ATTACK_RANGE && MathF.Abs(toOpponent.Y) <= ATTACK_RANGE;

        WatchForDodgeRoll(toOpponent);

        // Guarding takes over everything else, guessing wrong costs the dummy its turn
        if (_blockTimer > 0)
        {
            StopWalking();
            return InputFlags.RightBumper;
        }

        InputFlags input = InputFlags.None;

        // Walk up to the other player, also turn around when they have gotten behind us (rolled past etc.)
        bool facingAway = Player.Flipped != (toOpponent.X < 0);
        if ((distance > ATTACK_RANGE || facingAway) && _walkPauseTimer <= 0)
        {
            input |= toOpponent.X < 0 ? InputFlags.DPadLeft : InputFlags.DPadRight;
            _isWalking = true;
        }
        else
        {
            StopWalking();
        }

        if (!inRange)
        {
            // The reaction time starts over whenever they are out of range, so the player always gets the first go
            _attackTimer = MathF.Max(_attackTimer, REACTION_TIME);
        }
        else if (_attackTimer <= 0)
        {
            // Press one of the kicks, then leave a gap for the player to hit back
            _kickButton = Random.Shared.NextSingle() < 0.5f ? InputFlags.A : InputFlags.B;
            _kickTimer = TAP_DURATION;
            _attackTimer = MIN_ATTACK_DELAY + Random.Shared.NextSingle() * (MAX_ATTACK_DELAY - MIN_ATTACK_DELAY);
        }

        if (_kickTimer > 0) input |= _kickButton;
        if (_jumpTimer > 0) input |= InputFlags.DPadUp;

        return input;
    }

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
            _jumpTimer = TAP_DURATION;
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
        // Kicks land too quickly to react to, so just like a player the dummy has to guess
        float distance = MathF.Abs(Opponent.Transform.Position.X - Player.Transform.Position.X);
        if (distance <= ATTACK_RANGE * 2 && Random.Shared.NextSingle() < BLOCK_CHANCE)
        {
            _blockTimer = BLOCK_DURATION;
        }
    }
}
