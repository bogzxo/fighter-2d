using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Bogz.Logging.Loggers;

using Fighter2D.Logic;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.HIDL;
using Horizon.HIDL.Runtime;

namespace Fighter2D.Player.Controllers;

internal abstract class PlayerController(MoveList moveList) : IGameComponent
{
    internal MoveList MoveList => moveList;
    internal PlayerStateTracker StateTracker;
    public FightingMove CurrentMove { get; private set; }
    private readonly HIDLRuntime _runtime = new();

    public ManualResetEventSlim MoveAnimationFinishedEvent { get; init; } = new();

    public void Initialize()
    {
        Player = (Parent as Player)!;
        CurrentMove = MoveList.Idle;
        StateTracker = new(Player);

        SetupHIDLRuntime();
    }

    private void SetupHIDLRuntime()
    {
        _runtime.GlobalScope.DeclareSystem("_PLAYER_JUMP", new NativeFunctionValue((_, _) =>
        {
            if (StateTracker.IsGrounded && StateTracker.CurrentStatus == PlayerStatusType.Normal)
            {
                Player.PhysicsBody.ApplyImpulse(new(0, PlayerConfig.JUMP_IMPULSE));  // Negative because world Y goes down

                StateTracker.ResetFallDuration();
            }
            return new NullValue();
        }));

        _runtime.GlobalScope.DeclareSystem("_PLAYER_DASH", new NativeFunctionValue((_, _) =>
        {
            if (StateTracker.IsGrounded && StateTracker.CurrentStatus == PlayerStatusType.Normal)
            {
                float direction = Player.Flipped ? -1.0f : 1.0f;
                Player.PhysicsBody.ApplyImpulse(new Vector2(direction * PlayerConfig.WALK_SPEED * PlayerConfig.DASH_MULTIPLIER, 0));  // Dash impulse
            }
            return new NullValue();
        }));

        _runtime.Evaluate(@"
        let player = {
            jump: func() { _PLAYER_JUMP(); },
            dash: func() { _PLAYER_DASH(); }
        }", true);

        foreach (var (_, move) in MoveList.AllMoves)
        {
            if (move.Callback is not null)
            {
                _runtime.GlobalScope.Assign(move.Name, move.Callback.Value with { Environment = _runtime.GlobalScope });
            }
        }
    }

    internal abstract bool IsMoveHeld(FightingMove candidate);

    private void TryResumeHeldMove()
    {
        //if (CurrentMove.Name != MoveList.Idle.Name) return;

        //foreach (var candidate in MoveList.AllMoves.Values)
        //{
        //    if (candidate.Bindings.Length == 0 || !candidate.Loopable || !IsMoveHeld(candidate)) continue;

        //    FightingMove moveToExecute = candidate;
        //    if (!IsStanceValid(candidate.Stances))
        //    {
        //        if (candidate.StanceReroutes != null &&
        //            candidate.StanceReroutes.TryGetValue(StateTracker.CurrentStance, out string? reroutedName) &&
        //            MoveList.AllMoves.TryGetValue(reroutedName, out var reroutedMove))
        //        {
        //            moveToExecute = reroutedMove;
        //        }
        //        else continue;
        //    }

        //    CurrentMove = moveToExecute;
        //    Player.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();
        //    Player.SetAnimation(CurrentMove.Animation.Name);
        //    return;
        //}
    }

    public abstract void TryProcessNewInputs(float dt);

    public abstract void Update(float dt);

    public void UpdateState(float dt)
    {
        if (StateTracker.CurrentStatus != PlayerStatusType.Normal)
        {
            ProcessAnimationFrames(dt);
            Player.SetAnimation(CurrentMove.Animation.Name);
            return;
        }

        StateTracker.UpdatePhysicsState(dt, CurrentMove.Name.Equals("crouch", StringComparison.OrdinalIgnoreCase));
        StateTracker.UpdateStatus(dt);

        CheckHeavyLanding();

        if (!string.IsNullOrEmpty(CurrentMove.ReleaseMove) && !IsMoveHeld(CurrentMove))
        {
            if (MoveList.AllMoves.TryGetValue(CurrentMove.ReleaseMove, out var releaseMove))
            {
                ChangeToMove(releaseMove);
            }
        }

        TryProcessNewInputs(dt);
        ProcessAnimationFrames(dt);
        TryResumeHeldMove();

        switch (StateTracker.CurrentStance)
        {
            case Stance.Falling:
                Player.SetAnimation("fall");
                break;

            case Stance.Jumping:
                Player.SetAnimation("jump");
                break;

            case Stance.Standing:
            case Stance.Crouching:
            default:
                Player.SetAnimation(CurrentMove.Animation.Name);
                break;
        }
        Update(dt);
    }

    private void CheckHeavyLanding()
    {
        if (StateTracker is { DeltaOnGround: false, IsGrounded: true })
        {
            if (StateTracker.FallDuration > 0.2f && MoveList.AllMoves.TryGetValue("heavy_land", out var landMove))
            {
                ChangeToMove(landMove);
            }

            StateTracker.ResetFallDuration();
        }
    }

    protected void ChangeToMove(FightingMove newMove)
    {
        CurrentMove = newMove;
        Player.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();

        if (CurrentMove.Callback is not null)
        {
            var (succ, msg) = _runtime.Evaluate($"{CurrentMove.Name}();");
            if (!succ) ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, msg);
        }
    }

    private float _frameStepTimer = 0.0f;

    private void ProcessAnimationFrames(float dt)
    {
        _frameStepTimer += dt;
        if (_frameStepTimer > 1.0f / 30f)
        {
            _frameStepTimer = 0;

            var (finished, index) = Player.AnimationManager.IncrementFrame(CurrentMove.Animation.Name);

            if (finished)
            {
                if (StateTracker.CurrentStatus != PlayerStatusType.Normal) return;

                if (!string.IsNullOrEmpty(CurrentMove.NextMove) &&
                    MoveList.AllMoves.TryGetValue(CurrentMove.NextMove, out var next))
                {
                    ChangeToMove(next);
                }
                else if (CurrentMove.Loopable && IsMoveHeld(CurrentMove))
                {
                    Player.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();

                    if (CurrentMove.Callback is not null)
                    {
                        _runtime.Evaluate($"{CurrentMove.Name}();");
                    }
                }
                else
                {
                    CurrentMove = MoveList.Idle;

                    if (!(CurrentMove.Name.Equals("idle") ||
                          CurrentMove.Name.Equals("jump") ||
                          CurrentMove.Name.Equals("dodge_roll") ||
                          CurrentMove.Name.Equals("heavy_land") ||
                          CurrentMove.Name.Equals("crouch_start") ||
                          CurrentMove.Name.Equals("crouch") ||
                          CurrentMove.Name.Equals("run_start") ||
                          CurrentMove.Name.Equals("running") ||
                          CurrentMove.Name.Equals("run_stop")))
                    {
                        MoveAnimationFinishedEvent.Set();
                    }
                }
            }
        }
    }

    protected bool IsStanceValid(Stance candidateStances)
    {
        switch (StateTracker.CurrentStance)
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

    public void ApplyStun(float duration, string stunAnimationName = "stun")
    {
        StateTracker.ApplyStun(duration);
        ForceMoveAnimation(stunAnimationName);
    }

    public void TrapInCombo(FightingMove comboMove, float lockDuration)
    {
        StateTracker.ApplyComboTrap(lockDuration);
        CurrentMove = comboMove;
        Player.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();
    }

    private void ForceMoveAnimation(string animName)
    {
        if (MoveList.AllMoves.TryGetValue(animName, out var target))
        {
            CurrentMove = target;
        }
        Player.AnimationManager.Animations[CurrentMove.Animation.Name].ResetIndex();
    }

    public void UpdatePhysics(float dt)
    {
    }

    public bool Enabled { get; set; }
    public string Name { get; set; } = "Player Controller (Abstract)";
    public Entity Parent { get; set; }

    protected Player Player { get; private set; }

    public void Render(float dt, object? obj = null)
    {
    }
}