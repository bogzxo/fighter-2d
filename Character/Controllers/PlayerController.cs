using System;
using System.Numerics;


using Fighter2D.Character;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Logic.Routines;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

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
    /// <summary>
    /// Whether hits are blocked right now, which is up to the status of whatever move we are in.
    /// </summary>
    public bool IsGuarding => StateTracker.CurrentStatus == PlayerStatusType.Guarding;

    protected bool IsInControl =>
        StateTracker.CurrentStatus is not (PlayerStatusType.Stunned or PlayerStatusType.ComboTrapped);

    /// <summary>
    /// Whether a new move is allowed to start right now, this is the case exactly when the current move can be interrupted.
    /// </summary>
    protected bool CanStartMove =>
        IsInControl && (ActiveRoutine == null || (CurrentMove.Interruptible && CanInterrupt));

    protected FrameRoutine? ActiveRoutine;
    private float _frameStepTimer = 0.0f;

    public virtual void Initialize()
    {   
        Player = (Parent as Player)!;
        MoveList = Player.MoveList;
        StateTracker = new PlayerStateTracker(Player);
        CurrentMove = new FightingMove { Id = MoveIds.NONE };
        ChangeToMove(MoveIds.IDLE);
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

    /// <summary>
    /// Helper method for routines to find out how many frames an animation has, 1 for one the character doesn't have.
    /// </summary>
    public uint GetAnimationLength(string? animName)
    {
        if (animName is null || !Player.AnimationManager.Animations.TryGetValue(animName, out var animation)) return 1;

        return Math.Max(1, animation.Length);
    }

    public void ChangeToMove(string newMoveId, bool forceRestart = false)
    {
        // Test if we are attempting to do the same move we already are and only allow it if the restart flag is set.
        if (!forceRestart && CurrentMove.Id == newMoveId) return;

        // Test if the move we are attempting to change to exists, the moves are whatever the move files of the character say
        if (!MoveList.TryGetMove(newMoveId, out var newMove)) return;

        // Set the new move.
        CanInterrupt = false;
        CurrentMove = newMove;

        // The statuses set by a routine end with its move, even when it was cut short (rolling -> kick)
        if (IsInControl)
        {
            StateTracker.CurrentStatus = PlayerStatusType.Normal;
        }

        // If the move has phases, set them for execution.
        ActiveRoutine = newMove.Phases.Length > 0 ? new FrameRoutine(MoveRoutine.Run(newMove, this)) : null;

        // The move begins right now rather than on the next animation frame, so there is no delay on the input.
        ActiveRoutine?.Start();
    }
    /// <summary>
    /// This method is called by the move routines exactly on the frame when a hit should attempt to happen.
    /// </summary>
    /// <param name="frame">The frame of the animation the hit is thrown on, which is what says how far it reaches.</param>
    public void TryHit(uint frame)
    {
        // A player of another machine does not get to decide anything here, their hits arrive as messages (see ReceiveHit)
        if (this is NetworkedPlayerController) return;

        // A blow only lands on somebody it is thrown at: with our back to them it goes into thin air
        float toOpponent = Opponent.Transform.Position.X - Player.Transform.Position.X;
        if (toOpponent != 0.0f && (toOpponent < 0.0f) != Player.Flipped) return;

        // Test what we hit with on this frame against the box they can be hit in -> test if the player is blocking -> register the hit.
        Box strike = Player.StrikeBox(frame);
        if (!Player.StrikeFixture.TestIntersection(Opponent.HurtboxFixture, Player.Transform.Position, Opponent.Transform.Position)) return;

        // Where the two meet is where it landed
        strike.Overlaps(Opponent.HurtBox, out Vector2 impact);

        // Dodge rolls go straight through hits
        if (Opponent.Controller.StateTracker.CurrentStatus == PlayerStatusType.Invulnerable) return;

        // The effects come off the spot we hit, flying the way the kick was going
        float direction = Opponent.Transform.Position.X < Player.Transform.Position.X ? -1 : 1;

        if (Opponent.Controller is NetworkedPlayerController && FightScene.Network is { } network)
        {
            // What comes of it is up to the machine that plays them, we only draw what it looked like from here
            network.SendHit(CurrentMove.Id, direction);

            if (!Opponent.Controller.IsGuarding)
            {
                FightScene.Effects.Hit(impact, direction, heavy: CurrentMove.Knockback != Vector2.Zero);
                Jolt(this, Opponent.Controller, heavy: CurrentMove.Knockback != Vector2.Zero, blocked: false);
            }
            else
            {
                FightScene.Effects.Block(impact, direction);
                Jolt(this, Opponent.Controller, heavy: false, blocked: true);
            }
            return;
        }

        if (!Opponent.Controller.IsGuarding)
        {
            Opponent.Controller.AcknowledgeHit(this.CurrentMove, direction);
            FightScene.Effects.Hit(impact, direction, heavy: CurrentMove.Knockback != Vector2.Zero);
            Jolt(this, Opponent.Controller, heavy: CurrentMove.Knockback != Vector2.Zero, blocked: false);
        }
        else
        {
            FightScene.Effects.Block(impact, direction);
            Jolt(this, Opponent.Controller, heavy: false, blocked: true);
        }
    }

    /// <summary>
    /// This method is called when the player of another machine says they landed a move on us, whether it counts is ours to decide.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    public void ReceiveHit(string moveId, float direction)
    {
        if (!MoveList.TryGetMove(moveId, out var hitMove)) return;

        // Dodge rolls go straight through hits
        if (StateTracker.CurrentStatus == PlayerStatusType.Invulnerable) return;

        // Where on us it landed is known to whoever threw it, from here it is the middle of what there is to hit
        Vector2 impact = Player.HurtBox.Center;

        if (!IsGuarding)
        {
            AcknowledgeHit(hitMove, direction);
            FightScene.Effects.Hit(impact, direction, heavy: hitMove.Knockback != Vector2.Zero);
            Jolt(Opponent.Controller, this, heavy: hitMove.Knockback != Vector2.Zero, blocked: false);
        }
        else
        {
            FightScene.Effects.Block(impact, direction);
            Jolt(Opponent.Controller, this, heavy: false, blocked: true);
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
            ApplyStun(MathF.Max(0.25f, hitMove.StunDuration));
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

        // Between the rounds nobody gets to do anything: the buttons count as let go of and nothing new is started
        bool frozen = FightScene.Round is { PlayersFrozen: true };
        if (frozen)
        {
            // Whatever we were in the middle of (running, crouching) ends there, a blow that is already on its way is seen through
            if (!_frozen && IsInControl && StateTracker.CurrentStatus != PlayerStatusType.Attacking) ChangeToMove(MoveIds.IDLE);
            _frozen = true;

            ReleaseInputs();
            return;
        }
        _frozen = false;

        // Inputs are read even when we can't act on them, so they can be buffered until the current move lets go
        ReadInputs(dt);

        // Only allow new inputs to be processed if we are in control and the current move can be interrupted
        // @spd this will definetly interfere with youe ability to implement pary moves etc as it halts your method
        if (CanStartMove)
        {
            TryProcessNewInputs(dt);
        }
    }

    // Whether the rounds had us held still on the last update
    private bool _frozen;

    /// <summary>
    /// Called for as long as the player is held still between rounds, for controllers that remember which buttons are down.
    /// </summary>
    protected virtual void ReleaseInputs() { }

    /// <summary>
    /// Called between two rounds, the player starts the next one standing there as if nothing had happened: no stun, no half finished move.
    /// </summary>
    public void Reset()
    {
        if (Player is null) return;

        StateTracker = new PlayerStateTracker(Player);
        ActiveRoutine = null;
        CanInterrupt = false;
        _hitStop = 0.0f;
        _frameStepTimer = 0.0f;

        ChangeToMove(MoveIds.IDLE, forceRestart: true);
        ReleaseInputs();
    }

    // How long (in seconds) a blow holds both fighters on the frame it landed on, and for how long whoever took it is lit up by it
    private const float HIT_STOP = 0.07f, HEAVY_HIT_STOP = 0.13f, BLOCK_STOP = 0.04f;
    private const float FLASH_TIME = 0.22f;

    // What a blow turns whoever took it for a moment: drawn brighter than they are, which reads as white
    private static readonly Vector4 HitFlash = new(2.6f, 2.4f, 2.4f, 1.0f);
    private static readonly Vector4 BlockFlash = new(1.4f, 1.6f, 2.0f, 1.0f);

    private float _hitStop;

    /// <summary>
    /// Helper method to let a blow be felt: both fighters hang on the frame it landed on for a moment, and whoever took it flashes.
    /// What it does to them (damage, knockback) is none of this, only how it looks.
    /// </summary>
    private static void Jolt(PlayerController attacker, PlayerController victim, bool heavy, bool blocked)
    {
        float stop = blocked ? BLOCK_STOP : heavy ? HEAVY_HIT_STOP : HIT_STOP;
        attacker._hitStop = MathF.Max(attacker._hitStop, stop);
        victim._hitStop = MathF.Max(victim._hitStop, stop);

        victim.Player.Tint = blocked ? BlockFlash : HitFlash;
        victim.Player.TweenTint(Vector4.One, FLASH_TIME);
    }

    /// <summary>
    /// Helper method to process the frames of the move
    /// </summary>
    private void ProcessAnimationFrames(float dt)
    {
        // A blow that just landed holds the frame it landed on
        if (_hitStop > 0.0f)
        {
            _hitStop -= dt;
            return;
        }

        _frameStepTimer += dt;
        float frameTime = 1.0f / Player.Character.FrameRate;

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
                ChangeToMove(MoveIds.IDLE);
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
        ChangeToMove(MoveIds.HIT_STUN, forceRestart: true);
    }

    /// <summary>
    /// Helper method to trap the player in a move.
    /// </summary>
    public void TrapInCombo(string comboMoveId, float lockDuration)
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
            ChangeToMove(MoveIds.HEAVY_LAND, forceRestart: true);
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

}