using System;
using System.Linq;
using System.Numerics;

using Bogz.Logging.Loggers;

using Fighter2D.Logic;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.HIDL.Runtime;
using Horizon.Rendering.Particles;
using ImGuiNET;

namespace Fighter2D.Player;

internal class ControllablePlayerMoveManager : IGameComponent
{
    public bool Enabled { get; set; }
    public string Name { get; set; } = string.Empty;
    public Entity Parent { get; set; }

    public FightingMove CurrentMove { get; private set; }

    private readonly PlayerInputTracker _input;
    private readonly PlayerStateTracker _state;
    private readonly MoveList _moveList = new();
    private readonly Random _random = new();

    private IntervalRunner _particleRunner;

    private float _frameStepTimer = 0.0f;
    private float _totalEngineTime = 0.0f;
    private float _jumpImpulse = 2500f;  // Changed from force to impulse
    private float _speed = 2000f;        // Desired max speed

    public Player _playerInstance;
    
    public ControllablePlayerMoveManager(Player playerInstance, int gamepadIndex)
    {
        _playerInstance = playerInstance;
        _input = new PlayerInputTracker(gamepadIndex);
        _state = new PlayerStateTracker(_playerInstance);
        Enabled = true;
    }

    public void Initialize()
    {
        CurrentMove = _moveList.Idle;
        _playerInstance.AnimationManager.Enabled = false;

        SetupHIDLRuntime();
        SetupAmbienceParticleRunner();
    }

    #region Callbacks & Particles
    private void SetupAmbienceParticleRunner()
    {
        _particleRunner = new IntervalRunner(1 / 15.0f, () =>
        {
            for (int diagonal = 0; diagonal < 4; diagonal++)
            {
                float x = _random.NextSingle() * GameEngine.Instance.WindowManager.WindowSize.X + diagonal / 2.0f;
                float y = _random.NextSingle() * GameEngine.Instance.WindowManager.WindowSize.Y + diagonal / 2.0f;

                var position = GameEngine.Instance.ActiveCamera.ScreenToWorld(new Vector2(x, y));
                SpawnParticle(position, -Vector2.One, 0.2f);
            }
        });
    }

    private void SetupHIDLRuntime()
    {
        var globalScope = GameEngine.Instance.Debugger.Console.Runtime.GlobalScope;

        globalScope.DeclareSystem("_PLAYER_JUMP", new NativeFunctionValue((_, _) =>
        {
            if (_state.IsGrounded && _state.CurrentStatus == PlayerStatusType.Normal)
            {
                _playerInstance.PhysicsBody.ApplyImpulse(new (0, _jumpImpulse));  // Negative because world Y goes down

                _state.ResetFallDuration();
            }
            return new NullValue();
        }));

        globalScope.DeclareSystem("_PLAYER_DASH", new NativeFunctionValue((_, _) =>
        {
            if (_state.IsGrounded && _state.CurrentStatus == PlayerStatusType.Normal)
            {
                float direction = _playerInstance.Flipped ? -1.0f : 1.0f;
                _playerInstance.PhysicsBody.ApplyImpulse(new Vector2(direction * _speed * 1.75f, 0));  // Dash impulse

                for (int i = 0; i < 32; i++)
                {
                    SpawnParticle(_playerInstance.Transform.Position - new Vector2(0, 32), new Vector2(-direction * 2f, 0.5f), 0.8f);
                }
            }
            return new NullValue();
        }));

        GameEngine.Instance.Debugger.Console.Runtime.Evaluate(@"
        let player = {
            jump: func() { _PLAYER_JUMP(); },
            dash: func() { _PLAYER_DASH(); }
        }", true);

        foreach (var (_, move) in _moveList.Moves)
        {
            if (move.Callback is not null)
            {
                globalScope.Assign(move.Name, move.Callback.Value with { Environment = globalScope });
            }
        }
    }
    #endregion

    #region Loops
    public void UpdatePhysics(float dt)
    {
    }

    private void Physics(float dt)
    {
        _totalEngineTime += dt;
        _input.Update();
        _state.UpdatePhysicsState(dt, CurrentMove.Name.Equals("crouch", StringComparison.OrdinalIgnoreCase));
        _state.UpdateStatus(dt);

        CheckHeavyLanding();

        if (_state.CurrentStatus != PlayerStatusType.Normal)
        {
            ProcessAnimationFrames(dt);
            _playerInstance.SetAnimation(CurrentMove.Animation.Name);
            return;
        }

        if (!string.IsNullOrEmpty(CurrentMove.ReleaseMove) && !IsMoveHeld(CurrentMove))
        {
            if (_moveList.Moves.TryGetValue(CurrentMove.ReleaseMove, out var releaseMove))
            {
                ChangeToMove(releaseMove);
            }
        }

        TryProcessNewInputs();
        ProcessAnimationFrames(dt);
        TryResumeHeldMove();

        switch (_state.CurrentStance)
        {
            case Stance.Falling:
                _playerInstance.SetAnimation("fall");
                break;
            case Stance.Jumping:
                _playerInstance.SetAnimation("jump");
                break;
            case Stance.Standing:
            case Stance.Crouching:
            default:
                _playerInstance.SetAnimation(CurrentMove.Animation.Name);
                break;
        }
    }

    public void UpdateState(float dt)
    {
        _particleRunner?.UpdateState(dt);

        if (_state.CurrentStatus != PlayerStatusType.Normal) return;

        if (GameEngine.Instance.InputManager.KeyboardManager.IsKeyPressed(Silk.NET.Input.Key.R))
            _moveList.Reload();

        var movementDir = _input.GetMovementInput();
        if (movementDir.X != 0)
        {
            _playerInstance.Flipped = movementDir.X < 0;
        }

        // Apply velocity-based movement with damping
        if (movementDir.X != 0)
        {
            var targetVelocity = movementDir.X * _speed;
            var currentVelocityX = _playerInstance.PhysicsBody.Velocity.X;
            var velocityDiff = targetVelocity - currentVelocityX;
            
            // Apply force proportional to velocity difference for responsive control
            _playerInstance.PhysicsBody.ApplyForce(new Vector2(velocityDiff * _playerInstance.PhysicsBody.Mass * 5f, 0));
        }

        Physics(dt);
    }
    #endregion

    #region External Status API
    public void ApplyStun(float duration, string stunAnimationName = "stun")
    {
        _state.ApplyStun(duration);
        ForceMoveAnimation(stunAnimationName);
    }

    public void TrapInCombo(FightingMove comboMove, float lockDuration)
    {
        _state.ApplyComboTrap(lockDuration);
        CurrentMove = comboMove;
        _playerInstance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();
    }

    private void ForceMoveAnimation(string animName)
    {
        if (_moveList.Moves.TryGetValue(animName, out var target))
        {
            CurrentMove = target;
        }
        _playerInstance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();
    }
    #endregion

    #region Logic & Animation
    private void CheckHeavyLanding()
    {
        if (!_state.DeltaOnGround && _state.IsGrounded)
        {
            if (_state.FallDuration > 0.2f && _moveList.Moves.TryGetValue("heavy_land", out var landMove))
            {
                ChangeToMove(landMove);
            }

            for (int i = 0; i < 32; i++)
            {
                Vector2 randomDir = new Vector2((float)_random.NextDouble(), (float)_random.NextDouble());
                SpawnParticle(_playerInstance.Transform.Position + Vector2.UnitY * -64, randomDir);
            }
            _state.ResetFallDuration();
        }
    }

    private void ProcessAnimationFrames(float dt)
    {
        _frameStepTimer += dt;
        if (_frameStepTimer > 1.0f / 30f)
        {
            _frameStepTimer = 0;

            var (finished, index) = _playerInstance.AnimationManager.IncrementFrame(CurrentMove.Animation.Name);
            if (finished)
            {
                if (_state.CurrentStatus != PlayerStatusType.Normal) return;

                if (!string.IsNullOrEmpty(CurrentMove.NextMove) && _moveList.Moves.TryGetValue(CurrentMove.NextMove, out var next))
                {
                    ChangeToMove(next);
                }
                else if (CurrentMove.Loopable && IsMoveHeld(CurrentMove))
                {
                    _playerInstance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();

                    if (CurrentMove.Callback is not null)
                    {
                        GameEngine.Instance.Debugger.Console.Runtime.Evaluate($"{CurrentMove.Name}();");
                    }
                }
                else
                {
                    CurrentMove = _moveList.Idle;
                }
            }
        }
    }

    private void TryProcessNewInputs()
    {
        var buttons = _input.ConsumeFramePresses();
        if (buttons.Length == 0 || !CurrentMove.Interuptable) return;

        foreach (var candidate in _moveList.Moves.Values)
        {
            if (candidate.Bindings.Length == 0) continue;

            bool matched = candidate.UseAnyBindings
                ? candidate.Bindings.Any(buttons.Contains)
                : candidate.Bindings.All(buttons.Contains);

            if (matched)
            {
                FightingMove moveToExecute = candidate;

                if (!IsStanceValid(candidate.Stances))
                {
                    if (candidate.StanceReroutes != null &&
                        candidate.StanceReroutes.TryGetValue(_state.CurrentStance, out string? reroutedName) &&
                        _moveList.Moves.TryGetValue(reroutedName, out var reroutedMove))
                    {
                        moveToExecute = reroutedMove;
                    }
                    else continue;
                }

                if (CurrentMove.Name == moveToExecute.Name) continue;

                if (candidate.DoubleTap || moveToExecute.DoubleTap)
                {
                    bool isDoubleTap = false;
                    foreach (var btn in candidate.Bindings.Where(buttons.Contains))
                    {
                        if (_input.HasDoubleTap(btn, 0.3f))
                        {
                            isDoubleTap = true;
                            _input.ClearButtonHistory(btn);
                            break;
                        }
                    }
                    if (!isDoubleTap) continue;
                }

                ChangeToMove(moveToExecute);
                return;
            }
        }
    }

    private void TryResumeHeldMove()
    {
        if (CurrentMove.Name != _moveList.Idle.Name) return;

        foreach (var candidate in _moveList.Moves.Values)
        {
            if (candidate.Bindings.Length == 0 || !candidate.Loopable || !IsMoveHeld(candidate)) continue;

            FightingMove moveToExecute = candidate;
            if (!IsStanceValid(candidate.Stances))
            {
                if (candidate.StanceReroutes != null &&
                    candidate.StanceReroutes.TryGetValue(_state.CurrentStance, out string? reroutedName) &&
                    _moveList.Moves.TryGetValue(reroutedName, out var reroutedMove))
                {
                    moveToExecute = reroutedMove;
                }
                else continue;
            }

            CurrentMove = moveToExecute;
            _playerInstance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();
            _playerInstance.SetAnimation(CurrentMove.Animation.Name);
            return;
        }
    }

    private bool IsStanceValid(Stance candidateStances)
    {
        switch (_state.CurrentStance)
        {
            case Stance.Jumping:
                return candidateStances.HasFlag(Stance.Jumping);
            case Stance.Falling:
                return candidateStances.HasFlag(Stance.Falling);
            case Stance.Crouching:
                return candidateStances.HasFlag(Stance.Crouching) || candidateStances == Stance.Standing;
            default:
                return candidateStances == Stance.Standing || candidateStances.HasFlag(Stance.Crouching);
        }
    }

    private bool IsMoveHeld(FightingMove candidate)
    {
        if (candidate.Bindings.Length == 0) return false;

        return candidate.UseAnyBindings
            ? candidate.Bindings.Any(_input.IsButtonHeld)
            : candidate.Bindings.All(_input.IsButtonHeld);
    }

    private void ChangeToMove(FightingMove newMove)
    {
        CurrentMove = newMove;
        _playerInstance.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();

        if (CurrentMove.Callback is not null)
        {
            var (succ, msg) = GameEngine.Instance.Debugger.Console.Runtime.Evaluate($"{CurrentMove.Name}();");
            if (!succ) ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, msg);
        }
    }

    private void SpawnParticle(Vector2 pos, Vector2 dir, float blend = 0.5f)
    {
        float val = (_random.NextSingle() * MathF.PI * 2.0f) - MathF.PI;

        _playerInstance.Particles.Add(new Particle2D(
            new Vector2(MathF.Sin(val), MathF.Cos(val)) * (1.0f - blend) + dir * blend,
            pos
        ));
    }
    #endregion

    public void Render(float dt, object? obj = null)
    {
        if (ImGui.Begin("Player"))
        {
            // Display all relevant player information

            ImGui.Text("Player Information");
            ImGui.Text($"Current Move: {CurrentMove.Name}");
            ImGui.Text($"Current Stance: {_state.CurrentStance}");
            ImGui.Text($"Is Grounded: {_state.IsGrounded}");    
        }
    }
}