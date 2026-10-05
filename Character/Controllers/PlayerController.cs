using System;
using System.Numerics;

using Egui;

using Fighter2D.Character;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Logic.Routines;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Rendering.Particles;

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
    public Player Player { get; private set; }

    /// <summary>
    /// The player we are fighting against.
    /// </summary>
    public Player Opponent => Player == FightScene.OtherPlayer ? FightScene.ControlledPlayer : FightScene.OtherPlayer;
    
    public bool CanInterrupt { get; internal set; }

    /// <summary>
    /// Whether the player is free of the debuffs that take the controls away (stuns, combo traps).
    /// </summary>
    protected bool IsInControl =>
        StateTracker.CurrentStatus is not (PlayerStatusType.Stunned or PlayerStatusType.ComboTrapped);

    /// <summary>
    /// Whether a new move is allowed to start right now, this is the case exactly when the current move can be interrupted.
    /// </summary>
    protected bool CanStartMove =>
        IsInControl && (ActiveRoutine == null || (CurrentMove.Interruptible && CanInterrupt));

    protected FrameRoutine? ActiveRoutine;
    private PlayerMoveRoutines _moveRoutines;
    private float _frameStepTimer = 0.0f;

    public virtual void Initialize()
    {   
        Player = (Parent as Player)!;
        MoveList = Player.MoveList;
        StateTracker = new PlayerStateTracker(Player);
        _moveRoutines = new PlayerMoveRoutines(Player, this);

        MoveList.MovementMoves[MoveId.Idle].RoutineFactory = _moveRoutines.Idle;
        MoveList.MovementMoves[MoveId.Jump].RoutineFactory = _moveRoutines.Jump;
        MoveList.MovementMoves[MoveId.DodgeRoll].RoutineFactory = _moveRoutines.DodgeRoll;
        MoveList.MovementMoves[MoveId.Run].RoutineFactory = _moveRoutines.Run;
        MoveList.MovementMoves[MoveId.Crouch].RoutineFactory = _moveRoutines.Crouch;
        MoveList.MovementMoves[MoveId.Fall].RoutineFactory = _moveRoutines.Fall;
        
        MoveList.FightMoves[MoveId.Block].RoutineFactory = _moveRoutines.Block;
        MoveList.FightMoves[MoveId.KickLeft].RoutineFactory = _moveRoutines.Kick;
        MoveList.FightMoves[MoveId.KickRight].RoutineFactory = _moveRoutines.Kick;
        MoveList.FightMoves[MoveId.JumpKick].RoutineFactory = _moveRoutines.JumpKick;
        MoveList.FightMoves[MoveId.HitStun].RoutineFactory = _moveRoutines.HitStun;

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
        Player.AnimationManager.SetFrame(ActiveAnimation, 0);
        ActiveRoutine?.ResetFrame();
    }

    public void ChangeToMove(MoveId newMoveId, bool forceRestart = false)
    {
        // Test if we are attempting to do the same move we already are and only allow it if the restart flag is set.
        if (!forceRestart && CurrentMove.Id == newMoveId) return;

        // Test if the move we are attempting to change to exists (for when logic is moved back to HIDL)
        if (!MoveList.TryGetMove(newMoveId, out var newMove)) return;

        // Set the new move.
        CanInterrupt = false;
        CurrentMove = newMove;

        // The statuses set by a routine end with its move, even when it was cut short (rolling -> kick)
        if (IsInControl)
        {
            StateTracker.CurrentStatus = PlayerStatusType.Normal;
        }

        // If the move has a routine, set it for execution.
        ActiveRoutine = newMove.RoutineFactory != null ? new FrameRoutine(newMove.RoutineFactory()) : null;

        // The move begins right now rather than on the next animation frame, so there is no delay on the input.
        ActiveRoutine?.Start();
    }
    /// <summary>
    /// This method is called by the move routines exactly on the frame when a hit should attempt to happen.
    /// </summary>
    public void TryHit()
    {
        // Test the player hitbox intersection -> test if the player is blocking -> register the hit.
        bool intersects = this.Player.HitboxFixture.TestIntersection(Opponent.HitboxFixture,
            Player.Transform.Position, Opponent.Transform.Position);

        if (!intersects) return;

        // Dodge rolls go straight through hits
        if (Opponent.Controller.StateTracker.CurrentStatus == PlayerStatusType.Invulnerable) return;

        // The effects come off the spot we hit, flying the way the kick was going
        Vector2 impact = Opponent.Transform.Position + Opponent.HitboxFixture.Position;
        float direction = Opponent.Transform.Position.X < Player.Transform.Position.X ? -1 : 1;

        if (Opponent.Controller.CurrentMove.Id != MoveId.Block)
        {
            Opponent.Controller.AcknowledgeHit(this.CurrentMove, direction);
            FightScene.Effects.Hit(impact, direction, heavy: CurrentMove.Knockback != Vector2.Zero);
        }
        else
        {
            FightScene.Effects.Block(impact, direction);
        }
    }

    /// <summary>
    /// This method is called by the move routines of the other player the moment they start an attack.
    /// </summary>
    public virtual void PrepareForHit() {}

    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    private void AcknowledgeHit(FightingMove hitMove, float direction)
    {
        Console.WriteLine("Were HIT! OWIE");
        this.Player.Health = (byte)Math.Max(0, this.Player.Health - hitMove.Damage);

        // Launch us away from the attacker (jump kicks send us into the air)
        if (hitMove.Knockback != Vector2.Zero)
        {
            this.Player.PhysicsBody.ApplyImpulse(new Vector2(direction * hitMove.Knockback.X, hitMove.Knockback.Y));
        }

        if (hitMove.StunDuration > 0)
        {
            ApplyStun(hitMove.StunDuration);
        }
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

        // Inputs are read even when we can't act on them, so they can be buffered until the current move lets go
        ReadInputs(dt);

        // Only allow new inputs to be processed if we are in control and the current move can be interrupted
        // @spd this will definetly interfere with youe ability to implement pary moves etc as it halts your method
        if (CanStartMove)
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
        const float frameTime = 1.0f / 24f;

        while (_frameStepTimer >= frameTime)
        {
            // Tick the routine
            uint frame = ActiveRoutine?.Tick() ?? 0;
            _frameStepTimer -= frameTime;

            // Set the frame
            Player.AnimationManager.SetFrame(ActiveAnimation, frame);

            // Check if the routine is still running
            if (ActiveRoutine is not { IsFinished: true }) continue;

            // By here the routine has finished naturally so we reset state chganges (the debuffs run out on their own timer)
            if (IsInControl)
            {
                StateTracker.CurrentStatus = PlayerStatusType.Normal;
            }
            CanInterrupt = true;

            // Handle stance reroutes || return to Idle automatically
            if (CurrentMove.FinishReroutes != null &&
                CurrentMove.FinishReroutes.TryGetValue(StateTracker.CurrentStance, out var rerouteId))
            {
                ChangeToMove(rerouteId);
            }
            else
            {
                // Automatically return to Idle when an attack/move finishes! (I always make her finish)
                ChangeToMove(MoveId.Idle);
            }
        }
    }

    /// <summary>
    /// Helper method to apply a stun effect.
    /// </summary>
    public void ApplyStun(float duration)
    {
        // Being hit again doesn't start the stun over, otherwise there would be no getting out of it
        if (StateTracker.CurrentStatus == PlayerStatusType.Stunned) return;

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

        // Even a short drop throws up a bit of dust
        if (StateTracker.FallDuration > 0.05f)
        {
            FightScene.Effects.Land(Player.FeetPosition, StateTracker.FallDuration);
        }

        if (StateTracker.FallDuration > 0.2f)
        {
            ChangeToMove(MoveId.HeavyLand, forceRestart: true);
        }
        StateTracker.ResetFallDuration();
    }

    /// <summary>
    /// Called every update no matter the state of the player, for controllers which keep track of their inputs.
    /// </summary>
    protected virtual void ReadInputs(float dt) { }

    /// <summary>
    /// Called only while a new move is allowed to start, this is where controllers pick the next move.
    /// </summary>
    protected virtual void TryProcessNewInputs(float dt) { }

    /// <summary>
    /// Tests if the buttons are held down, the move routines use this to know when to end (letting go of block etc.)
    /// </summary>
    public virtual bool IsHeld(InputFlags buttons) => false;

    public virtual void UpdatePhysics(float dt) { }
    public virtual void Render(float dt, object? obj = null) { }

    public virtual void RenderUi(Ui root)
    {
    }
}