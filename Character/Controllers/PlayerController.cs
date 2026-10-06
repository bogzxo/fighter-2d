using System;

using Fighter2D.Combat;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;

using Horizon.Core;
using Horizon.Core.Components;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// The brain of a player. It reads the buttons of its input, matches them against the move list and plays the move they start.
/// There is one of these per player and they are all the same, who presses the buttons is up to the <see cref="IPlayerInput"/> it is given.
/// Everything runs on fixed ticks (see PlayerConfig.TICK_RATE) no matter how fast the game is drawn.
/// </summary>
internal sealed class PlayerController(IPlayerInput input) : IGameComponent
{
    // The most ticks one update catches up on. After a hitch the fight carries on from where it is rather than fast forwarding
    private const int MAX_TICKS_PER_UPDATE = 8;

    private static readonly FightingMove NoMove = new() { Id = MoveIds.NONE };

    public IPlayerInput Input { get; } = input;
    public PlayerInputs Inputs { get; } = new();
    public Player Player { get; private set; } = null!;
    public MoveList MoveList { get; private set; } = null!;
    public PlayerStateTracker StateTracker { get; private set; } = null!;
    public MovePlayback Playback { get; private set; } = null!;
    public PlayerMovement Movement { get; private set; } = null!;

    public FightingMove CurrentMove => Playback?.Move ?? NoMove;
    public string ActiveAnimation { get; private set; } = "idle";

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Player Controller";
    public Entity Parent { get; set; } = null!;

    /// <summary>
    /// The player we are fighting against.
    /// </summary>
    public Player Opponent => Fight.OpponentOf(Player);

    /// <summary>
    /// How many ticks of the fight this player has been through. Inputs and snapshots that go over the network are stamped with it.
    /// </summary>
    public uint Tick { get; private set; }

    /// <summary>
    /// Whether the player is played on another machine. Their buttons are played back here, but their hits and their health are decided over there.
    /// </summary>
    public bool IsRemote => Input.IsRemote;

    /// <summary>
    /// Whether the move we are in can be cancelled into another one right now, its phases set this.
    /// </summary>
    public bool CanCancel { get; internal set; }

    /// <summary>
    /// Whether the move we are in has already thrown its hit.
    /// </summary>
    public bool HitThrown { get; private set; }

    public bool IsBlocking => StateTracker.CurrentStatus == PlayerStatusType.Blocking;

    /// <summary>
    /// Whether we are in the startup of an attack. Getting hit now is a counter hit.
    /// </summary>
    public bool IsInStartup => StateTracker.CurrentStatus == PlayerStatusType.Attacking && !HitThrown;

    /// <summary>
    /// Whether we are in the recovery of an attack, which is when we are wide open for a punish.
    /// </summary>
    public bool IsRecovering => StateTracker.CurrentStatus == PlayerStatusType.Attacking && HitThrown;

    /// <summary>
    /// Whether the player has the controls, hitstun takes them away.
    /// </summary>
    public bool IsInControl => !StateTracker.IsInHitstun;

    /// <summary>
    /// Whether a new move is allowed to start right now.
    /// </summary>
    public bool CanStartMove => IsInControl && _hitstop == 0 && CurrentMove.Cancellable && CanCancel;

    /// <summary>
    /// Whether the player can walk right now, and whether they can turn around. Both are up to the move they are in.
    /// A block can't be turned around, you have to let go of it first.
    /// </summary>
    public bool CanSteer => IsInControl && CurrentMove.AllowsSteering;
    public bool CanTurn => IsInControl && CurrentMove.AllowsTurning && !IsBlocking;

    private float _tickTimer;

    // How far into the frame of animation that is showing we are, the next one is due at 1
    private float _frameProgress;

    // How many more ticks a hit that just landed holds us frozen for
    private int _hitstop;

    // Whether the rounds had us held still on the last tick
    private bool _frozen;

    // Whether the move is being stepped right now, a move that starts from in there keeps the rhythm going
    private bool _stepping;

    public void Initialize()
    {
        Player = (Parent as Player)!;
        MoveList = Player.MoveList;
        StateTracker = new PlayerStateTracker(Player);
        Playback = new MovePlayback(this);
        Movement = new PlayerMovement(this);

        Input.Attach(this);
        ChangeToMove(MoveIds.IDLE, forceRestart: true);
    }

    public void UpdateState(float dt)
    {
        _tickTimer += dt;

        for (int ticks = 0; _tickTimer >= PlayerConfig.TICK_TIME; ticks++)
        {
            if (ticks == MAX_TICKS_PER_UPDATE)
            {
                _tickTimer = 0.0f;
                break;
            }

            _tickTimer -= PlayerConfig.TICK_TIME;
            Step();
        }
    }

    public void UpdatePhysics(float dt) => Movement?.UpdatePhysics(dt);

    /// <summary>
    /// One tick of the fight for this player.
    /// </summary>
    private void Step()
    {
        Tick++;

        // Between rounds nobody gets to do anything
        bool frozen = Fight.PlayersFrozen;
        Inputs.Push(Tick, ReadInput(frozen));

        // Update physiscs cues such as crouching, falling and grounded
        StateTracker.UpdatePhysicsState(PlayerConfig.TICK_TIME);

        if (_hitstop > 0)
        {
            // Hitstop freezes the move and the hitstun both
            _hitstop--;
        }
        else
        {
            // The hitstun move lasts exactly as long as the hitstun does
            if (StateTracker.TickHitstun() && CurrentMove.Id == MoveIds.HIT_STUN) FinishMove();

            AdvanceMove();
        }

        Movement.CheckLanding();

        if (frozen)
        {
            // Whatever we were in the middle of (running, crouching) ends here, but an attack that is already out gets to finish
            if (!_frozen && IsInControl && StateTracker.CurrentStatus != PlayerStatusType.Attacking) ChangeToMove(MoveIds.IDLE);
            _frozen = true;
            return;
        }
        _frozen = false;

        // Face the way we are steering before any move is matched, so a move with a direction (the roll) goes the way it was tapped
        Movement.UpdateFacing();

        if (CanStartMove) TryStartMove();
    }

    /// <summary>
    /// Helper method to read what is held this tick. Inputs are read even when we can't act on them so they can be buffered.
    /// </summary>
    private InputFlags ReadInput(bool frozen)
    {
        // A network player's inputs always get taken off the pile, or they would all be waiting when the round starts
        InputFlags held = frozen && !IsRemote ? InputFlags.None : Input.Read();

        return frozen ? InputFlags.None : held;
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
        // The statuses set by a move end with it (hitstun runs out on its own timer)
        if (IsInControl) StateTracker.CurrentStatus = PlayerStatusType.Normal;
        CanCancel = true;

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

        CanCancel = false;
        HitThrown = false;

        // The statuses set by a move end with it, even when it was cancelled (rolling -> kick)
        if (IsInControl) StateTracker.CurrentStatus = PlayerStatusType.Normal;

        // A move that was asked for gets the whole of its first frame, however far along the last one was
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
        if (!MoveList.TryMatchInput(Inputs.Buffer, StateTracker.CurrentStance, out var move, out var signature)) return;

        // A press only ever starts one move, held inputs (run, crouch, block) keep matching for as long as they are held
        if (move.Trigger != InputTrigger.Held)
        {
            Inputs.Buffer.Consume(signature);
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

    /* Animation */

    /// <summary>
    /// Helper method for the moves to change the animation that is playing.
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
    /// Helper method to draw the frame the move is on. A move that outlasts its animation holds the last frame of it.
    /// </summary>
    private void ShowFrame()
    {
        if (!Player.AnimationManager.Animations.ContainsKey(ActiveAnimation)) return;

        Player.AnimationManager.SetFrame(ActiveAnimation, ClampFrame(Playback.Shown));
    }

    private uint ClampFrame(uint frame) => Math.Min(frame, GetAnimationLength(ActiveAnimation) - 1);

    /* Hitting and getting hit, what a hit comes to is up to the HitResolver */

    /// <summary>
    /// Called by the move exactly on the frame its hit comes out.
    /// </summary>
    /// <param name="frame">The frame of the animation the hit comes out on, which is what its hitbox is read from.</param>
    public void ThrowHit(uint frame)
    {
        HitThrown = true;
        HitResolver.TryLand(this, ClampFrame(frame));
    }

    /// <summary>
    /// Called by the moves of the other player the moment they start an attack.
    /// </summary>
    public void OnOpponentAttack() => Input.OnOpponentAttack();

    /// <summary>
    /// Takes the controls away for a number of ticks. Whatever we were doing ends here and we sit in the hitstun move until it is over.
    /// </summary>
    public void ApplyHitstun(int ticks)
    {
        StateTracker.ApplyHitstun(ticks);

        // Every hit restarts the move, so each hit of a combo is seen to land
        ChangeToMove(MoveIds.HIT_STUN, forceRestart: true);
    }

    /// <summary>
    /// Freezes the player on the frame they are on for a number of ticks.
    /// </summary>
    public void ApplyHitstop(int ticks) => _hitstop = Math.Max(_hitstop, ticks);

    /* Rounds and the network */

    /// <summary>
    /// Called between two rounds. The player starts the next one standing there as if nothing had happened.
    /// </summary>
    public void Reset()
    {
        if (Player is null) return;

        StateTracker = new PlayerStateTracker(Player);
        CanCancel = false;
        _hitstop = 0;
        _frameProgress = 0.0f;
        Movement.Reset();

        ChangeToMove(MoveIds.IDLE, forceRestart: true);
        Inputs.Release();
    }

    /// <summary>
    /// Drops a network player onto the exact frame of the move their own machine has them on, see PlayerReconciler.
    /// </summary>
    public void RestoreMove(FightingMove move, int phase, uint frame, string animation, uint shown, bool canCancel, bool hitThrown)
    {
        Playback.Restore(move, phase, frame, animation, shown);
        CanCancel = canCancel;
        HitThrown = hitThrown;
        _frameProgress = 0.0f;
        ShowFrame();
    }

    public void Render(float dt, object? obj = null) { }
}
