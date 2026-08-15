using System;

using Fighter2D.Character;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Logic.Routines;

using Horizon.Core;
using Horizon.Core.Components;

using Silk.NET.Input;

namespace Fighter2D.Character.Controllers;
/// <summary>
/// Abstract implementation of a player controller which contains logic for executing and transitioning between move states.
/// </summary>
internal abstract class PlayerController : IGameComponent
{
    public MoveList MoveList { get; private set; }
    public PlayerStateTracker StateTracker { get; private set; }
    public FightingMove CurrentMove { get; private set; }

    public string ActiveAnimation { get; private set; } = "idle";

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Player Controller";
    public Entity Parent { get; set; }
    protected Player Player { get; private set; }

    public bool CanInterupt { get; internal set; }

    protected FrameRoutine? ActiveRoutine;
    private PlayerMoveRoutines _moveRoutines;
    private float _frameStepTimer = 0.0f;

    public virtual void Initialize()
    {   
        Player = (Parent as Player)!;
        MoveList = Player.MoveList;
        StateTracker = new PlayerStateTracker(Player);
        _moveRoutines = new PlayerMoveRoutines(Player, this);

        MoveList.AllMoves[MoveId.KickLeft].RoutineFactory = _moveRoutines.KickLeft;
        MoveList.AllMoves[MoveId.JumpKick].RoutineFactory = _moveRoutines.JumpKick;
        MoveList.AllMoves[MoveId.Jump].RoutineFactory = _moveRoutines.Jump;
        MoveList.AllMoves[MoveId.DodgeRoll].RoutineFactory = _moveRoutines.DodgeRoll;
        MoveList.AllMoves[MoveId.Run].RoutineFactory = _moveRoutines.Run;
        MoveList.AllMoves[MoveId.Block].RoutineFactory = _moveRoutines.Block;
        MoveList.AllMoves[MoveId.Crouch].RoutineFactory = _moveRoutines.Crouch;
        MoveList.AllMoves[MoveId.Fall].RoutineFactory = _moveRoutines.Fall;

        CurrentMove = new FightingMove { Id = (MoveId)(-1) };
        ChangeToMove(MoveId.Idle);
    }

    /// <summary>
    /// Helper method for routines to force an animation to be played.
    /// </summary>
    public void PlayAnimation(string animName)
    {
        ActiveAnimation = animName;
        Player.SetAnimation(ActiveAnimation);
    }

    public void ChangeToMove(MoveId newMoveId, bool forceRestart = false)
    {
        // Test if we are attempting to do the same move we already are and only allow it if the restart flag is set.
        if (!forceRestart && CurrentMove.Id == newMoveId) return;

        // Test if the move we are attempting to change to exists (for when logic is moved back to HIDL)
        if (!MoveList.TryGetMove(newMoveId, out var newMove)) return;

        // Set the new move.
        CanInterupt = false;
        CurrentMove = newMove;
        PlayAnimation(newMove.AnimationName);

        // If the move has a routine, set it for execution.
        ActiveRoutine = newMove.RoutineFactory != null ? new FrameRoutine(newMove.RoutineFactory()) : null;
    }

    public void UpdateState(float dt)
    {
        // Update animation frames and handle stance reroutes
        ProcessAnimationFrames(dt);

        // Update physiscs cues such as crouching, falling and grounded
        StateTracker.UpdatePhysicsState(dt);

        // Update status duration times to handle stun debuffs etc.
        StateTracker.UpdateStatus(dt);

        // Check if the landing should be heavy and apply the appropriote debuff
        CheckHeavyLanding();

        // Only allow new inputs to be processed if we are 'normal' and currently in control
        // @spd this will definetly interfere with youe ability to implement pary moves etc as it halts your method
        if (StateTracker.CurrentStatus == PlayerStatusType.Normal &&
           (CurrentMove.Interruptible || ActiveRoutine == null))
        {
            TryProcessNewInputs(dt);
        }
    }

    /// <summary>
    /// Helper method to process the frames of the move
    /// </summary>
    private void ProcessAnimationFrames(float dt)
    {
        _frameStepTimer += dt;
        const float frameTime = 1.0f / 24f; // 24 FPS animations

        // @bogz @spd inspect if substepping frames is a good idea for a fighting game
        while (_frameStepTimer >= frameTime)
        //if (_frameStepTimer >= frameTime)
        {
            int frame = ActiveRoutine?.Tick() ?? 0;

            // Correctly handle fractions of a frame instead of snapping to the next frame between frames
            //_frameStepTimer = 0;
            _frameStepTimer -= frameTime;

            // Step the ACTIVE animation, not the move's default starting animation
            Player.AnimationManager.SetFrame(ActiveAnimation, frame, true);

            // Check if we need to automatically reroute to a different move depending on the stance transition
            if (ActiveRoutine is not { IsFinished: true }) return;

            // Handle stance reroutes (Jumping -> falling)
            if (CurrentMove.StanceReroutes != null &&
                CurrentMove.StanceReroutes.TryGetValue(StateTracker.CurrentStance, out var rerouteId))
            {
                ChangeToMove(rerouteId);
            }
            else CanInterupt = true;
        }
    }

    /// <summary>
    /// Helper method to apply a stun effect.
    /// </summary>
    public void ApplyStun(float duration)
    {
        StateTracker.ApplyStun(duration);
        ChangeToMove(MoveId.HitStun, forceRestart: true);
    }

    /// <summary>
    /// Helper method to trap the player in a move.
    /// </summary>
    public void TrapInCombo(MoveId comboMoveId, float lockDuration)
    {
        StateTracker.ApplyComboTrap(lockDuration);
        ChangeToMove(comboMoveId, forceRestart: true);
    }

    /// <summary>
    /// Helper method to test if the player has fallen more than 0.2s, in which case a debuff is applied.
    /// </summary>
    private void CheckHeavyLanding()
    {
        if (StateTracker is not { IsGrounded: true, DeltaOnGround: false }) return;
        if (StateTracker.FallDuration > 0.2f)
        {
            ChangeToMove(MoveId.HeavyLand, forceRestart: true);
        }
        StateTracker.ResetFallDuration();
    }

    public abstract void TryProcessNewInputs(float dt);
    public abstract bool IsButtonHeld(ButtonName btn);
    public abstract void UpdatePhysics(float dt);
    public virtual void Render(float dt, object? obj = null) { }
}