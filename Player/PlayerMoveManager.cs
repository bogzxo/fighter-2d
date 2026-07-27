using System;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Collections.Generic;

using CumInstinctDuel.Logic;

using Horizon.Core;
using Horizon.Core.Collections;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.HIDL;
using Horizon.HIDL.Runtime;
using Horizon.Input.Components;
using Horizon.Rendering.Particles;

using ImGuiNET;

using Silk.NET.Input;

using Bogz.Logging.Loggers;

namespace CumInstinctDuel.Player;

internal class PlayerMoveManager : IGameComponent
{
    #region Public Properties
    public FightingMove CurrentMove => _currentMove;
    public bool Enabled { get; set; }
    public string Name { get; set; } = string.Empty;
    public Entity Parent { get; set; }

    public bool IsGrounded { get; private set; }

    // Expose current stance and status for external systems (AI, UI, Hitboxes)
    public Stance CurrentStance => _currentStance;
    public PlayerStatusType CurrentStatus => _currentStatus;
    #endregion

    #region Private Fields 
    private readonly MoveList _moveList = new();
    private readonly Random _random = new();
    private readonly List<ButtonName> _buttonPresses = [];

    private FightingMove _currentMove;
    private IGamepad? _gamepad = null;
    private IntervalRunner _runner;

    private float _frameStepTimer = 0.0f;
    private float _jumpForce = 2000f;
    private float _speed = 2500f;

    // State & Status Tracking
    private Stance _currentStance = Stance.Standing;
    private PlayerStatusType _currentStatus = PlayerStatusType.Normal;
    private float _statusTimer = 0.0f;
    private bool deltaOnGround = false;
    #endregion

    #region Initialization
    public void Initialize()
    {
        SetupHIDLRuntime();
        _currentMove = _moveList.Idle;
        Player.Instance.AnimationManager.Enabled = false;

        SetupAmbienceParticleRunner();
    }

    private void SetupAmbienceParticleRunner()
    {
        _runner = new IntervalRunner(1 / 15.0f, () =>
        {
            (float, float) Roll(int diag) => (
                _random.NextSingle() * GameEngine.Instance.WindowManager.WindowSize.X + diag / 2.0f,
                _random.NextSingle() * GameEngine.Instance.WindowManager.WindowSize.Y + diag / 2.0f
            );

            for (int diagonal = 0; diagonal < 4; diagonal++)
            {
                var (x, y) = Roll(diagonal);
                var position = GameEngine.Instance.ActiveCamera.ScreenToWorld(new Vector2(x, y));
                SpawnParticle(position, -Vector2.One, 0.2f);
            }
        });
    }

    private void SetupHIDLRuntime()
    {
        GameEngine.Instance.Debugger.Console.Runtime.GlobalScope.DeclareSystem("_PLAYER_JUMP", new NativeFunctionValue((_, _) =>
        {
            if (IsGrounded && _currentStatus == PlayerStatusType.Normal)
            {
                Player.Instance.PlayerBody.ApplyLinearImpulseToCenter(new Vector2(0, _jumpForce));
                _currentStance = Stance.Jumping;
            }
            return new NullValue();
        }));

        GameEngine.Instance.Debugger.Console.Runtime.Evaluate(@"
let player = {
    jump: func() {
        _PLAYER_JUMP();
    }
}", true);

        foreach (var (_, move) in _moveList.Moves)
        {
            if (move.Callback is not null)
            {
                GameEngine.Instance.Debugger.Console.Runtime.GlobalScope.Assign(move.Name, move.Callback.Value with { Environment = GameEngine.Instance.Debugger.Console.Runtime.GlobalScope });
            }
        }
    }
    #endregion

    #region Loops
    public void UpdatePhysics(float dt)
    {
        CheckGround();

        // 1. Update status effects timer (Stun/Combos recovery)
        UpdateStatus(dt);

        // 2. Update dynamic air/ground states (Jumping / Falling / Standing)
        UpdateStanceState();

        CheckJumpLand();
        AcquireGamepad();

        // If player is stunned or trapped in a combo, suppress normal move logic/input matching
        if (_currentStatus != PlayerStatusType.Normal)
        {
            MoveHandler(dt);
            Player.Instance.SetAnimation(CurrentMove.Animation.Name);
            return;
        }

        if (TryChangeMove())
        {
            Player.Instance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();

            if (CurrentMove.Callback is not null)
            {
                var (succ, msg) = GameEngine.Instance.Debugger.Console.Runtime.Evaluate(CurrentMove.Name + "();");
                if (!succ) ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, msg);
            }

            GameEngine.Instance.Debugger.Console.Log($"Matched Move '{CurrentMove.Name}'");
        }

        MoveHandler(dt);
        TryResumeHeldMove();

        Player.Instance.SetAnimation(CurrentMove.Animation.Name);
    }

    private void CheckGround()
    {
        IsGrounded = false;

        // 1. Velocity gate: If moving upward rapidly, we are airborne
        float verticalVelocity = Player.Instance.PlayerBody.GetLinearVelocity().Y;
        if (verticalVelocity > 0.15f) return;

        var body = Player.Instance.PlayerBody;
        if (body == null) return;

        // 2. Iterate through all active contacts on the player's body
        for (var edge = body.GetContactList(); edge != null; edge = edge.next)
        {
            var contact = edge.contact;

            // Skip if not actively touching or disabled
            if (contact == null || !contact.IsTouching()) continue;

            // 3. Check if either fixture involved in this collision is our Foot Sensor
            var fixtureA = contact.FixtureA;
            var fixtureB = contact.FixtureB;

            bool isFootContact = (fixtureA.UserData as string == "FootSensor") ||
                                 (fixtureB.UserData as string == "FootSensor");

            if (isFootContact)
            {
                IsGrounded = true;
                return;
            }
        }
    }

    private void UpdateStanceState()
    {
        float verticalVelocity = Player.Instance.PlayerBody.GetLinearVelocity().Y;

        if (IsGrounded)
        {
            if (_currentStance == Stance.Jumping || _currentStance == Stance.Falling)
            {
                _currentStance = Stance.Standing;
            }
        }
        else
        {
            // Moving upward after jump impulse
            if (verticalVelocity > 0.1f)
            {
                _currentStance = Stance.Jumping;
            }
            // Moving downward (Falling state active until landing)
            else if (verticalVelocity <= -0.1f)
            {
                _currentStance = Stance.Falling;
            }
        }
    }

    private void UpdateStatus(float dt)
    {
        if (_currentStatus == PlayerStatusType.Normal) return;

        _statusTimer -= dt;
        if (_statusTimer <= 0)
        {
            // Recover back to normal fighting state
            ClearStatus();
        }
    }

    private void CheckJumpLand()
    {
        if (deltaOnGround != IsGrounded)
        {
            for (int i = 0; i < 32; i++)
            {
                Vector2 randomDir = new Vector2((float)_random.NextDouble(), (float)_random.NextDouble());
                SpawnParticle(Player.Instance.Transform.Position + Vector2.UnitY * -64, randomDir);
            }
        }

        deltaOnGround = IsGrounded;
    }

    public void UpdateState(float dt)
    {
        _runner?.UpdateState(dt);

        // Restrict movement input capability if stunned or combo-locked
        if (_currentStatus != PlayerStatusType.Normal) return;

        var movementDir = GetMovementInput();
        if (movementDir.X != 0)
        {
            Player.Instance.Flipped = movementDir.X < 0;
        }

        Player.Instance.PlayerBody.ApplyLinearImpulseToCenter(movementDir * Vector2.UnitX * dt * _speed);
    }
    #endregion

    #region Extension Hooks: Stun & Combo Management
    /// <summary>
    /// Applies a stun effect for a given duration, forcing an interrupt and locking inputs.
    /// </summary>
    public void ApplyStun(float duration, string stunAnimationName = "stun")
    {
        _currentStatus = PlayerStatusType.Stunned;
        _statusTimer = duration;
        ForceMoveAnimation(stunAnimationName);
    }

    /// <summary>
    /// Traps the player in a combo sequence, rendering them unable to act until released or finished.
    /// </summary>
    public void TrapInCombo(FightingMove comboMove, float lockDuration)
    {
        _currentStatus = PlayerStatusType.ComboTrapped;
        _statusTimer = lockDuration;
        _currentMove = comboMove;
        Player.Instance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();
    }

    private void ClearStatus()
    {
        _currentStatus = PlayerStatusType.Normal;
        _statusTimer = 0.0f;
    }

    private void ForceMoveAnimation(string animName)
    {
        // Fallback safety lookup or override for damage/stun states
        if (_moveList.Moves.ContainsKey(animName))
        {
            _currentMove = _moveList.Moves[animName];
        }
        Player.Instance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();
    }
    #endregion

    #region Movement and Animation Logic 
    private void MoveHandler(float dt)
    {
        _frameStepTimer += dt;
        if (_frameStepTimer > 1.0f / 25f)
        {
            _frameStepTimer = 0;

            var (finished, index) = Player.Instance.AnimationManager.IncrementFrame(CurrentMove.Animation.Name);
            if (finished)
            {
                if (_currentStatus != PlayerStatusType.Normal)
                {
                    // Status animations might loop or hold on last frame depending on design
                    return;
                }

                if (CurrentMove.Loopable && IsMoveHeld(CurrentMove))
                {
                    Player.Instance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();

                    if (CurrentMove.Callback is not null)
                    {
                        GameEngine.Instance.Debugger.Console.Runtime.Evaluate(CurrentMove.Name + "(); playerJump();");
                    }
                }
                else
                {
                    _currentMove = _moveList.Idle;
                }
            }
        }
    }

    private bool TryChangeMove()
    {
        if (_buttonPresses.Count > 0)
        {
            var buttons = _buttonPresses.ToArray();
            _buttonPresses.Clear();

            if (CurrentMove.Interuptable)
            {
                foreach (var (name, candidate) in _moveList.Moves)
                {
                    if (candidate.Bindings.Length == 0) continue;

                    // **Stance Validation**: Ensure the candidate move can be executed in our current stance (e.g. Jumping / Falling)
                    if (!candidate.Stances.HasFlag(_currentStance)) continue;

                    bool matched = candidate.UseAnyBindings
                        ? candidate.Bindings.Any(btn => buttons.Contains(btn))
                        : candidate.Bindings.All(btn => buttons.Contains(btn));

                    if (matched && !_currentMove.Name.Equals(candidate.Name))
                    {
                        _currentMove = candidate;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private void TryResumeHeldMove()
    {
        if (_currentMove.Name != _moveList.Idle.Name) return;

        foreach (var (name, candidate) in _moveList.Moves)
        {
            if (candidate.Bindings.Length == 0) continue;

            // Validate Stance compatibility
            if (!candidate.Stances.HasFlag(_currentStance)) continue;

            if (candidate.Loopable && IsMoveHeld(candidate))
            {
                _currentMove = candidate;
                Player.Instance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();
                Player.Instance.SetAnimation(CurrentMove.Animation.Name);

                GameEngine.Instance.Debugger.Console.Log($"Resumed Held Move '{CurrentMove.Name}'");
                return;
            }
        }
    }
    #endregion

    #region Input & Helpers 
    private void AcquireGamepad()
    {
        if (_gamepad is null && GameEngine.Instance.InputManager.NativeInputContext?.Gamepads.Count > 0)
        {
            _gamepad = GameEngine.Instance.InputManager.NativeInputContext.Gamepads[0];
            _gamepad.ButtonDown += (_, args) =>
            {
                // Disallow button inputs if stunned or combo trapped
                if (_currentStatus == PlayerStatusType.Normal)
                {
                    _buttonPresses.Add(args.Name);
                }
            };
        }
    }

    internal Vector2 GetMovementInput()
    {
        var joystick = XInputJoystickInputManager.Gamepad;
        return new Vector2(
            joystick.DPadLeft().Pressed ? -1 : joystick.DPadRight().Pressed ? 1 : 0,
            joystick.DPadUp().Pressed ? 1 : joystick.DPadDown().Pressed ? -1 : 0
        );
    }

    private bool IsButtonHeld(ButtonName btn)
    {
        return _gamepad?.Buttons.Any(b => b.Name == btn && b.Pressed) ?? false;
    }

    private bool IsMoveHeld(FightingMove candidate)
    {
        if (candidate.Bindings.Length == 0) return false;

        return candidate.UseAnyBindings
            ? candidate.Bindings.Any(IsButtonHeld)
            : candidate.Bindings.All(IsButtonHeld);
    }

    private void SpawnParticle(Vector2 pos, Vector2 dir, float blend = 0.5f)
    {
        float val = ((_random.NextSingle() * 2.0f) - MathF.PI);

        Player.Instance.Particles.Add(new Particle2D(
            new Vector2(MathF.Sin(val), MathF.Cos(val)) * (1.0f - blend) + dir * blend,
            pos
        ));
    }

    public void Render(float dt, object? obj = null)
    {
        if (ImGui.Begin("Player Movement Manager"))
        {
            ImGui.Text($"Current Move: {CurrentMove.Name}");
            ImGui.Text($"Current Stance: {_currentStance}");
            ImGui.Text($"Player Status: {_currentStatus}");
            ImGui.Text($"Player Pos: {Player.Instance.Transform.Position}");
            ImGui.Text($"Grounded: {IsGrounded}");

            ImGui.DragFloat("Player Speed", ref _speed);
            ImGui.DragFloat("Player Jump", ref _jumpForce);

            ImGui.End();
        }
    }
    #endregion
}