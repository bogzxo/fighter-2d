using System;

using Fighter2D.Character;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Logic.Routines;

using Horizon.Core;
using Horizon.Core.Components;

using Silk.NET.Input;

namespace Fighter2D.Character.Controllers;

internal abstract class PlayerController : IGameComponent
{
    public MoveList MoveList { get; private set; }
    public PlayerStateTracker StateTracker { get; private set; }
    public FightingMove CurrentMove { get; private set; }

    // CRITICAL: Track the currently playing animation dynamically!
    public string ActiveAnimation { get; private set; } = "idle";

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Player Controller";
    public Entity Parent { get; set; }
    protected Player Player { get; private set; }

    private FrameRoutine? _activeRoutine;
    private PlayerMoveRoutines _moveRoutines;
    private float _frameStepTimer = 0.0f;

    protected PlayerController(MoveList moveList)
    {
        MoveList = moveList;
    }

    public virtual void Initialize()
    {
        Player = (Parent as Player)!;
        StateTracker = new PlayerStateTracker(Player);
        _moveRoutines = new PlayerMoveRoutines(Player, this);

        MoveList.AllMoves[MoveId.KickLeft].RoutineFactory = _moveRoutines.KickLeft;
        MoveList.AllMoves[MoveId.JumpKick].RoutineFactory = _moveRoutines.JumpKick;
        MoveList.AllMoves[MoveId.Jump].RoutineFactory = _moveRoutines.Jump;
        MoveList.AllMoves[MoveId.DodgeRoll].RoutineFactory = _moveRoutines.DodgeRoll;
        MoveList.AllMoves[MoveId.Run].RoutineFactory = _moveRoutines.Run;
        MoveList.AllMoves[MoveId.Block].RoutineFactory = _moveRoutines.Block;
        MoveList.AllMoves[MoveId.Crouch].RoutineFactory = _moveRoutines.Crouch;

        CurrentMove = new FightingMove { Id = (MoveId)(-1) };
        ChangeToMove(MoveId.Idle);
    }

    // Helper method so coroutines can safely change animations mid-move
    public void PlayAnimation(string animName)
    {
        ActiveAnimation = animName;
        Player.SetAnimation(ActiveAnimation);
    }

    public void ChangeToMove(MoveId newMoveId, bool forceRestart = false)
    {
        if (!forceRestart && CurrentMove != null && CurrentMove.Id == newMoveId) return;

        if (MoveList.TryGetMove(newMoveId, out var newMove))
        {
            CurrentMove = newMove;

            // Start the initial animation using our new synchronized method
            PlayAnimation(newMove.AnimationName);

            if (newMove.RoutineFactory != null)
            {
                _activeRoutine = new FrameRoutine(newMove.RoutineFactory());
            }
            else
            {
                _activeRoutine = null;
            }
        }
    }

    public void UpdateState(float dt)
    {
        ProcessAnimationFrames(dt);
        //int frame = _activeRoutine?.Tick() ?? 0;
        if (_activeRoutine?.IsFinished == true && CurrentMove.Id != MoveId.Idle)
        {
            ChangeToMove(MoveId.Idle);
        }

        bool isCrouching = CurrentMove.Id == MoveId.Crouch;
        StateTracker.UpdatePhysicsState(dt, isCrouching);
        StateTracker.UpdateStatus(dt);

        CheckHeavyLanding();

        if (StateTracker.CurrentStatus == PlayerStatusType.Normal &&
           (CurrentMove.Interruptible || _activeRoutine == null))
        {
            TryProcessNewInputs(dt);
        }
    }

    private void ProcessAnimationFrames(float dt)
    {
        _frameStepTimer += dt;
        float frameTime = 1.0f / 24f; // 24 FPS animations

        //while (_frameStepTimer >= frameTime)
        if (_frameStepTimer >= frameTime)
        {
            int frame = _activeRoutine?.Tick() ?? 0;

            _frameStepTimer = 0;
            //_frameStepTimer -= frameTime;

            // Step the ACTIVE animation, not the move's default starting animation
            var finished = Player.AnimationManager.SetFrame(ActiveAnimation, frame, true);

            if (finished)
            {
                // If a routine is actively controlling the player, let it finish on its own time.
                // Resetting the index here causes the active animation to Loop continuously.
                if (_activeRoutine != null)
                {
                    //Player.AnimationManager.Animations[ActiveAnimation].ResetIndex();
                    return;
                }

                if (StateTracker.CurrentStatus != PlayerStatusType.Normal)
                {
                    //Player.AnimationManager.Animations[ActiveAnimation].ResetIndex();
                    return;
                }

                if (CurrentMove.Id != MoveId.Idle)
                {
                    ChangeToMove(MoveId.Idle);
                }
                else
                {
                    //Player.AnimationManager.Animations[ActiveAnimation].ResetIndex();
                }
            }
        }
    }

    public void ApplyStun(float duration)
    {
        StateTracker.ApplyStun(duration);
        ChangeToMove(MoveId.HitStun, forceRestart: true);
    }

    public void TrapInCombo(MoveId comboMoveId, float lockDuration)
    {
        StateTracker.ApplyComboTrap(lockDuration);
        ChangeToMove(comboMoveId, forceRestart: true);
    }

    private void CheckHeavyLanding()
    {
        if (StateTracker.IsGrounded && !StateTracker.DeltaOnGround)
        {
            if (StateTracker.FallDuration > 0.2f)
            {
                ChangeToMove(MoveId.HeavyLand, forceRestart: true);
            }
            StateTracker.ResetFallDuration();
        }
    }

    public abstract void TryProcessNewInputs(float dt);
    public abstract bool IsButtonHeld(ButtonName btn);
    public abstract void UpdatePhysics(float dt);
    public void Render(float dt, object? obj = null) { }
}