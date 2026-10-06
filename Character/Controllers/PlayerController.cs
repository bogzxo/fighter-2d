using System;
using System.Numerics;

using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Networking;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Core.Components;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// What plays a player: it reads the buttons of its input, matches them against the move list and plays the move they
/// start, frame by frame. There is one of these for every player and they are all the same, whoever presses the buttons
/// (a gamepad, the dummy, a player on another machine) is up to the <see cref="IPlayerInput"/> it is given.
/// Everything about the fight goes by its ticks (see PlayerConfig.TICK_RATE), however often the game is drawn.
/// </summary>
internal sealed class PlayerController(IPlayerInput input) : IGameComponent
{
    public const float TICK_TIME = 1.0f / PlayerConfig.TICK_RATE;

    // The most ticks an update catches up on, after a hitch the fight carries on from where it is rather than racing after it
    private const int MAX_TICKS_PER_UPDATE = 8;

    // How many ticks of what was held are kept, for telling the other machine (see InputAt)
    private const int INPUT_HISTORY = 32;

    // How far (in frames) the move of a player of another machine may be off from where their machine has it and be left alone
    private const int SYNC_FRAME_TOLERANCE = 2;

    // How far (in units of the world) a player of another machine may be off from where their machine has them and be
    // left alone, and from how far off they are put there in one go rather than eased over
    private const float SYNC_DEAD_ZONE = 3.0f, SYNC_SNAP_DISTANCE = 96.0f;

    // How fast a player of another machine is eased over to where they belong, the higher the sooner
    private const float SYNC_EASE = 12.0f;

    public IPlayerInput Input { get; } = input;
    public MoveList MoveList { get; private set; } = null!;
    public PlayerStateTracker StateTracker { get; private set; } = null!;
    public MovePlayback Playback { get; private set; } = null!;
    public Player Player { get; private set; } = null!;

    public FightingMove CurrentMove => Playback?.Move ?? _noMove;
    private static readonly FightingMove _noMove = new() { Id = MoveIds.NONE };

    public string ActiveAnimation { get; private set; } = "idle";

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Player Controller";
    public Entity Parent { get; set; } = null!;

    /// <summary>
    /// The player we are fighting against.
    /// </summary>
    public Player Opponent => Player == FightScene.OtherPlayer ? FightScene.ControlledPlayer : FightScene.OtherPlayer;

    /// <summary>
    /// How many ticks of the fight this player has been through, which is what the inputs and the states that go to the other machine are told apart by.
    /// </summary>
    public uint Tick { get; private set; }

    /// <summary>
    /// Whether the player is played on another machine: their buttons are pressed here as they come in, what comes of
    /// their blows and how they really are doing is decided over there.
    /// </summary>
    public bool IsRemote => Input.IsRemote;

    /// <summary>
    /// Whether the move we are in has let go of us, set by its phases (see MovePhase.Interrupt).
    /// </summary>
    public bool CanInterrupt { get; internal set; }

    /// <summary>
    /// Whether the move we are in has thrown its blow already.
    /// </summary>
    public bool HasStruck { get; private set; }

    /// <summary>
    /// Whether hits are blocked right now, which is up to the status of whatever move we are in. Only the ones that come from in front are.
    /// </summary>
    public bool IsGuarding => StateTracker.CurrentStatus == PlayerStatusType.Guarding;

    /// <summary>
    /// Whether we are winding up a blow that hasn't been thrown yet. Whoever hits us now has beaten us to it.
    /// </summary>
    public bool IsCommitted => StateTracker.CurrentStatus == PlayerStatusType.Attacking && !HasStruck;

    /// <summary>
    /// Whether the player is free of the debuffs that take the controls away (stuns).
    /// </summary>
    public bool IsInControl => !StateTracker.IsStunned;

    /// <summary>
    /// Whether a new move is allowed to start right now, this is the case exactly when the current move can be interrupted.
    /// </summary>
    public bool CanStartMove => IsInControl && _hitStop == 0 && CurrentMove.Interruptible && CanInterrupt;

    /// <summary>
    /// Whether the player can walk right now, and whether they can turn around. Both are up to the move they are in,
    /// and a guard stays up to the side it was raised to: turning it round takes letting go of it.
    /// </summary>
    public bool CanSteer => IsInControl && CurrentMove.AllowsSteering;
    public bool CanTurn => IsInControl && CurrentMove.AllowsTurning && !IsGuarding;

    private readonly InputBuffer _inputs = new(PlayerConfig.INPUT_BUFFER_TICKS, PlayerConfig.DOUBLE_TAP_TICKS);
    private readonly InputFlags[] _history = new InputFlags[INPUT_HISTORY];

    private float _tickTimer;

    // How far into the frame of animation that is showing we are, the next one is due at 1
    private float _frameProgress;

    // For how many more ticks a blow that just landed holds us on the frame it landed on
    private int _hitStop;

    // Whether the rounds had us held still on the last tick
    private bool _frozen;

    // Whether the move is being stepped right now, one that starts from in there carries on in its rhythm
    private bool _stepping;

    // How far a player of another machine is from where they belong, eased away by the physics
    private Vector2 _positionError;

    public void Initialize()
    {
        Player = (Parent as Player)!;
        MoveList = Player.MoveList;
        StateTracker = new PlayerStateTracker(Player);
        Playback = new MovePlayback(this);

        Input.Attach(this);
        ChangeToMove(MoveIds.IDLE, forceRestart: true);
    }

    public void UpdateState(float dt)
    {
        _tickTimer += dt;

        for (int ticks = 0; _tickTimer >= TICK_TIME; ticks++)
        {
            if (ticks == MAX_TICKS_PER_UPDATE)
            {
                _tickTimer = 0.0f;
                break;
            }

            _tickTimer -= TICK_TIME;
            Step();
        }
    }

    /// <summary>
    /// One tick of the fight for this player.
    /// </summary>
    private void Step()
    {
        Tick++;

        // Between the rounds nobody gets to do anything: the buttons count as let go of and nothing new is started
        bool frozen = FightScene.Round is { PlayersFrozen: true };

        // Inputs are read even when we can't act on them, so they can be buffered until the current move lets go.
        // What comes in from another machine is always taken off the pile, or it would all be waiting when the round starts
        InputFlags held = frozen && !IsRemote ? InputFlags.None : Input.Read();
        if (frozen) held = InputFlags.None;

        _inputs.Push(held);
        _history[Tick % INPUT_HISTORY] = held;

        // Update physiscs cues such as crouching, falling and grounded
        StateTracker.UpdatePhysicsState(TICK_TIME);

        if (_hitStop > 0)
        {
            // A blow that just landed holds everything on the frame it landed on: the move, and the stun it brought
            _hitStop--;
        }
        else
        {
            // The stun move lasts exactly as long as the stun does
            if (StateTracker.TickStun() && CurrentMove.Id == MoveIds.HIT_STUN) FinishMove();

            AdvanceMove();
        }

        // Check if the landing should be heavy and apply the appropriote debuff
        CheckHeavyLanding();

        if (frozen)
        {
            // Whatever we were in the middle of (running, crouching) ends there, a blow that is already on its way is seen through
            if (!_frozen && IsInControl && StateTracker.CurrentStatus != PlayerStatusType.Attacking) ChangeToMove(MoveIds.IDLE);
            _frozen = true;
            return;
        }
        _frozen = false;

        // Face the way we are steering, this happens before any move is matched so any move that has a direction like the roll goes the way it was tapped
        UpdateFacing();

        // Only allow new inputs to be processed if we are in control and the current move can be interrupted
        if (CanStartMove) TryStartMove();
    }

    /* Moves */

    /// <summary>
    /// Helper method to play the frames of the move that are due on this tick.
    /// </summary>
    private void AdvanceMove()
    {
        _frameProgress += Player.Character.FrameRate / PlayerConfig.TICK_RATE;
        _stepping = true;

        while (_frameProgress >= 1.0f)
        {
            _frameProgress -= 1.0f;

            // A move that is over hands its frame to whatever comes after it
            if (!Playback.Step()) FinishMove();
        }

        _stepping = false;
        ShowFrame();
    }

    /// <summary>
    /// Helper method for what follows a move that has run its course.
    /// </summary>
    private void FinishMove()
    {
        // The statuses set by a move end with it (the debuffs run out on their own timer)
        if (IsInControl) StateTracker.CurrentStatus = PlayerStatusType.Normal;
        CanInterrupt = true;

        // Handle stance reroutes || return to Idle automatically
        if (CurrentMove.FinishReroutes != null &&
            CurrentMove.FinishReroutes.TryGetValue(StateTracker.CurrentStance, out var rerouteId))
        {
            ChangeToMove(rerouteId, forceRestart: true);
        }
        else
        {
            // Automatically return to Idle when an attack/move finishes! (I always make her finish)
            ChangeToMove(MoveIds.IDLE, forceRestart: true);
        }
    }

    public void ChangeToMove(string newMoveId, bool forceRestart = false)
    {
        // Test if we are attempting to do the same move we already are and only allow it if the restart flag is set.
        if (!forceRestart && CurrentMove.Id == newMoveId) return;

        // Test if the move we are attempting to change to exists, the moves are whatever the move files of the character say
        if (!MoveList.TryGetMove(newMoveId, out var newMove)) return;

        CanInterrupt = false;
        HasStruck = false;

        // The statuses set by a move end with it, even when it was cut short (rolling -> kick)
        if (IsInControl) StateTracker.CurrentStatus = PlayerStatusType.Normal;

        // A move that was asked for starts on its first frame and gets all of it, however far the one before had got
        if (!_stepping) _frameProgress = 0.0f;

        // The move begins right now rather than on the next animation frame, so there is no delay on the input.
        Playback.Begin(newMove);
        ShowFrame();
    }

    /// <summary>
    /// Helper method to start the move the buttons are asking for, if they are asking for one.
    /// </summary>
    private void TryStartMove()
    {
        // Find a match as per the order of precedence defined in the movelist
        if (!MoveList.TryMatchInput(_inputs, StateTracker.CurrentStance, out var move, out var signature)) return;

        // A press only ever starts one move, held inputs (run, crouch, block) keep matching for as long as they are held
        if (move.Trigger != InputTrigger.Held)
        {
            _inputs.Consume(signature);
        }

        // Check stance reroutes (hitting kick while in the air -> jumpkick)
        if (move.StanceReroutes != null &&
            move.StanceReroutes.TryGetValue(StateTracker.CurrentStance, out string? rerouteId))
        {
            ChangeToMove(rerouteId);
            return;
        }

        ChangeToMove(move.Id);
    }

    /// <summary>
    /// Helper method for the moves to change the animation that is playing, which starts on its first frame.
    /// </summary>
    public void PlayAnimation(string animName)
    {
        ActiveAnimation = animName;
        Player.SetAnimation(ActiveAnimation);
    }

    /// <summary>
    /// Helper method for the moves to find out how many frames an animation has, 1 for one the character doesn't have.
    /// </summary>
    public uint GetAnimationLength(string? animName)
    {
        if (animName is null || !Player.AnimationManager.Animations.TryGetValue(animName, out var animation)) return 1;

        return Math.Max(1, animation.Length);
    }

    /// <summary>
    /// Helper method to have the frame drawn that the move is on. A move that goes on for longer than its animation
    /// stays on the last frame of it.
    /// </summary>
    private void ShowFrame()
    {
        if (!Player.AnimationManager.Animations.ContainsKey(ActiveAnimation)) return;

        Player.AnimationManager.SetFrame(ActiveAnimation, ClampFrame(Playback.Shown));
    }

    private uint ClampFrame(uint frame) => Math.Min(frame, GetAnimationLength(ActiveAnimation) - 1);

    /* Facing and walking */

    /// <summary>
    /// The way the player is steering: -1 for left, 1 for right and 0 for neither.
    /// </summary>
    private float SteeringInput => (_inputs.IsHeld(InputFlags.DPadRight) ? 1 : 0) - (_inputs.IsHeld(InputFlags.DPadLeft) ? 1 : 0);

    /// <summary>
    /// This is the one place a player is turned around by their buttons, whether they may is up to <see cref="CanTurn"/>.
    /// </summary>
    private void UpdateFacing()
    {
        if (!CanTurn) return;

        float direction = SteeringInput;
        if (direction != 0) Player.Flipped = direction < 0;
    }

    public void UpdatePhysics(float dt)
    {
        if (Player?.PhysicsBody is not { } body) return;

        if (_positionError != Vector2.Zero)
        {
            // Eased over rather than put there, a player that jumps about is worse than one that is a little off
            Vector2 step = _positionError * MathF.Min(1.0f, dt * SYNC_EASE);
            body.Position += step;
            _positionError -= step;
        }

        // rudementary temporary move logic
        float direction = CanSteer ? SteeringInput : 0;
        if (direction == 0) return;

        var targetVelocity = direction * Player.Character.WalkSpeed * Input.WalkSpeedScale;
        var velocityDiff = targetVelocity - body.Velocity.X;

        body.ApplyForce(new Vector2(velocityDiff * body.Mass * 5f, 0));
    }

    /* Hitting and being hit, what a blow comes to is up to Combat */

    /// <summary>
    /// This method is called by the move exactly on the frame when a hit should attempt to happen.
    /// </summary>
    /// <param name="frame">The frame of the animation the hit is thrown on, which is what says how far it reaches.</param>
    public void TryHit(uint frame)
    {
        HasStruck = true;

        // A player of another machine does not get to decide anything here, their hits arrive as messages (see FightNetwork)
        if (IsRemote) return;

        // A blow only lands on somebody it is thrown at: with our back to them it goes into thin air
        if (!Player.IsFacing(Opponent)) return;

        // Test what we hit with on this frame against the box they can be hit in
        Box strike = Player.StrikeBox(ClampFrame(frame));
        if (!Player.StrikeFixture.TestIntersection(Opponent.HurtboxFixture, Player.Transform.Position, Opponent.Transform.Position)) return;

        // Where the two meet is where it landed
        strike.Overlaps(Opponent.HurtBox, out Vector2 impact);

        // The effects come off the spot we hit, flying the way the kick was going
        float direction = Opponent.Transform.Position.X < Player.Transform.Position.X ? -1 : 1;

        Combat.Land(this, Opponent.Controller, CurrentMove, direction, impact);
    }

    /// <summary>
    /// This method is called by the moves of the other player the moment they start an attack.
    /// </summary>
    public void PrepareForHit() => Input.OnOpponentAttack();

    /// <summary>
    /// Takes the controls away for a number of ticks: whatever we were doing ends there, and we are in the stun move until it is over.
    /// </summary>
    public void Stun(int ticks)
    {
        StateTracker.ApplyStun(ticks);

        // Every blow starts it over, so each one of a combo is seen to land
        ChangeToMove(MoveIds.HIT_STUN, forceRestart: true);
    }

    /// <summary>
    /// Holds the player on the frame they are on for a number of ticks, see Combat for what does.
    /// </summary>
    public void Freeze(int ticks) => _hitStop = Math.Max(_hitStop, ticks);

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

        // Somebody who comes down stunned stays in their stun
        if (StateTracker.FallDuration > 0.2f && IsInControl)
        {
            ChangeToMove(MoveIds.HEAVY_LAND, forceRestart: true);
        }
        StateTracker.ResetFallDuration();
    }

    /* Inputs */

    /// <summary>
    /// Tests if the buttons are held down, the moves use this to know when to end (letting go of block etc.)
    /// </summary>
    public bool IsHeld(InputFlags buttons) => _inputs.IsHeld(buttons);

    /// <summary>
    /// What was held on a tick, for telling the other machine. Only the last few are remembered.
    /// </summary>
    public InputFlags InputAt(uint tick) => _history[tick % INPUT_HISTORY];

    /* Rounds and the other machine */

    /// <summary>
    /// Called between two rounds, the player starts the next one standing there as if nothing had happened: no stun, no half finished move.
    /// </summary>
    public void Reset()
    {
        if (Player is null) return;

        StateTracker = new PlayerStateTracker(Player);
        CanInterrupt = false;
        _hitStop = 0;
        _frameProgress = 0.0f;
        _positionError = Vector2.Zero;

        ChangeToMove(MoveIds.IDLE, forceRestart: true);
        _inputs.Push(InputFlags.None);
    }

    /// <summary>
    /// This method is called for a player of another machine with how their machine has them, as of the tick whose
    /// buttons were last pressed here. Wherever what we made of those buttons has come apart from it, theirs goes.
    /// </summary>
    public void Reconcile(in PlayerSnapshot snapshot)
    {
        Player.Flipped = snapshot.Flipped;
        StateTracker.Restore(snapshot.Status, snapshot.StunTicks, snapshot.ComboHits);

        bool inStep = CurrentMove.Id == snapshot.MoveId
            && Playback.Phase == snapshot.Phase
            && Math.Abs((int)Playback.Frame - snapshot.Frame) <= SYNC_FRAME_TOLERANCE;

        if (!inStep && MoveList.TryGetMove(snapshot.MoveId, out var move))
        {
            // Put on the very frame they are on over there, which stays put if that is what their move does with it
            Playback.Restore(move, snapshot.Phase, (uint)snapshot.Frame, snapshot.Animation, snapshot.Shown);
            CanInterrupt = snapshot.CanInterrupt;
            HasStruck = snapshot.HasStruck;
            _frameProgress = 0.0f;
            ShowFrame();
        }

        var body = Player.PhysicsBody;
        Vector2 error = snapshot.Position - body.Position;

        if (error.LengthSquared() > SYNC_SNAP_DISTANCE * SYNC_SNAP_DISTANCE)
        {
            // Too far off to walk it back (a lag spike, a new round)
            body.Position = snapshot.Position;
            body.SetVelocity(snapshot.Velocity);
            _positionError = Vector2.Zero;
        }
        else
        {
            _positionError = error.LengthSquared() > SYNC_DEAD_ZONE * SYNC_DEAD_ZONE ? error : Vector2.Zero;
        }
    }

    public void Render(float dt, object? obj = null) { }
}
