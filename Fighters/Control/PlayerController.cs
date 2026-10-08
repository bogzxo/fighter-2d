using System;
using System.Numerics;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Fighters.Control;

/// <summary>
/// The brain of a player. It reads the buttons of its input, matches them against the move list and plays the move they start.
/// There is one of these per player and they are all the same, who presses the buttons is up to the <see cref="IPlayerInput"/> it is given.
/// Everything runs on fixed ticks (see FightTicks.TICK_RATE) no matter how fast the game is drawn.
/// </summary>
internal sealed class PlayerController(IPlayerInput input) : GameComponent
{
    // The most ticks one update catches up on. After a hitch the fight carries on from where it is rather than fast forwarding
    private const int MAX_TICKS_PER_UPDATE = 8;

    private static readonly FightingMove NoMove = new() { Id = MoveIds.NONE };

    // What somebody who got knocked out is dimmed to, and how long (in seconds) that takes
    private static readonly Vector4 KnockedOutTint = new(0.62f, 0.62f, 0.72f, 1.0f);
    private const float KNOCKED_OUT_FADE = 0.5f;

    public IPlayerInput Input { get; } = input;
    public PlayerInputs Inputs { get; } = new();
    public Player Player { get; private set; } = null!;
    public MoveList MoveList { get; private set; } = null!;
    public FighterState State { get; private set; } = null!;
    public MovePlayback Playback { get; private set; } = null!;
    public PlayerMovement Movement { get; private set; } = null!;

    /// <summary>
    /// What we do over the body of whoever we just knocked out, see <see cref="VictoryTaunt"/>.
    /// </summary>
    public VictoryTaunt Taunt { get; private set; } = null!;

    public FightingMove CurrentMove => Playback?.Move ?? NoMove;
    public string ActiveAnimation { get; private set; } = "idle";

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

    public bool IsBlocking => State.CurrentStatus == FighterStatus.Blocking;

    /// <summary>
    /// Whether we are in the startup of an attack. Getting hit now is a counter hit.
    /// </summary>
    public bool IsInStartup => State.CurrentStatus == FighterStatus.Attacking && !HitThrown;

    /// <summary>
    /// Whether we are in the recovery of an attack, which is when we are wide open for a punish.
    /// </summary>
    public bool IsRecovering => State.CurrentStatus == FighterStatus.Attacking && HitThrown;

    /// <summary>
    /// Whether the player is out of health. Somebody who is stays down until the round is reset.
    /// </summary>
    public bool IsKnockedOut => Player.Health == 0;

    /// <summary>
    /// Whether the player has the controls. Hitstun takes them away, and so does being knocked out.
    /// </summary>
    public bool IsInControl => !State.IsInHitstun && !IsKnockedOut;

    /// <summary>
    /// Whether a new move is allowed to start right now. Not in hitstop, not stuck in a block that just ate a hit, and in a move that lets go.
    /// </summary>
    public bool CanStartMove => IsInControl && _hitstop == 0 && !State.IsInBlockstun && CurrentMove.Cancellable && CanCancel;

    /// <summary>
    /// Whether the player can walk right now, and whether they can turn around. Both are up to the move they are in.
    /// A block can't be turned around, you have to let go of it first.
    /// </summary>
    public bool CanSteer => IsInControl && CurrentMove.AllowsSteering;
    public bool CanTurn => IsInControl && CurrentMove.AllowsTurning && !IsBlocking;

    // How much of the next tick of the fight has gone by, in thousandths of one. Counted in whole numbers rather than
    // by adding up seconds: a sixtieth of a second made of two halves of one in floats comes up a hair short every so
    // often, and the fight stands still for an update and then takes two ticks in one, which looks like arse
    private const int TICK_PARTS = 1000;
    private long _tickParts;

    // How far into the frame of animation that is showing we are, the next one is due at 1
    private float _frameProgress;

    // How many more ticks a hit that just landed holds us frozen for
    private int _hitstop;

    // Whether the rounds had us held still on the last tick
    private bool _frozen;

    // Whether the move is being stepped right now, a move that starts from in there keeps the rhythm going
    private bool _stepping;

    // Whether we were knocked out on the last tick, for spotting the tick we go down on
    private bool _wasKnockedOut;

    public override void Initialize()
    {
        Player = (Parent as Player)!;
        MoveList = Player.MoveList;
        State = new FighterState(Player);
        Playback = new MovePlayback(this);
        Movement = new PlayerMovement(this);
        Taunt = new VictoryTaunt(this);

        Input.Attach(this);
        ChangeToMove(MoveIds.IDLE, forceRestart: true);
    }

    public override void UpdateState(float dt)
    {
        _tickParts += (long)Math.Round(dt * FightTicks.TICK_RATE * TICK_PARTS);

        for (int ticks = 0; _tickParts >= TICK_PARTS; ticks++)
        {
            if (ticks == MAX_TICKS_PER_UPDATE)
            {
                _tickParts = 0;
                break;
            }

            _tickParts -= TICK_PARTS;
            Step();
        }
    }

    public override void UpdatePhysics(float dt) => Movement?.UpdatePhysics(dt);

    /// <summary>
    /// One tick of the fight for this player.
    /// </summary>
    private void Step()
    {
        Tick++;

        // Between rounds nobody gets to do anything
        bool frozen = Fight.PlayersFrozen;
        Inputs.Push(Tick, ReadInput(frozen));

        // Update the physics side of the state, crouching, falling and grounded
        State.UpdatePhysicsState(FightTicks.TICK_TIME);

        if (_hitstop > 0)
        {
            // Hitstop freezes the move and the hitstun both
            _hitstop--;
        }
        else
        {
            // Somebody who is knocked out never gets out of hitstun, for everybody else the hitstun move (or lying on the floor)
            // lasts exactly as long as the hitstun does. The blockstun runs down alongside, the block move lets go when it is out
            State.TickBlockstun();

            if (IsKnockedOut) StayDown();
            else if (State.TickHitstun() && CurrentMove.Id is MoveIds.HIT_STUN or MoveIds.KNOCKED_DOWN) FinishMove();

            AdvanceMove();
        }

        Movement.CheckLanding();

        if (frozen)
        {
            // Whatever we were in the middle of (running, crouching) ends here, but an attack that is already out gets to finish
            if (!_frozen && IsInControl && State.CurrentStatus != FighterStatus.Attacking) ChangeToMove(MoveIds.IDLE);
            _frozen = true;

            // Unless there is somebody on the floor to gloat over
            Taunt.Update();
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
        if (!frozen) return Input.Read();

        // A network player's inputs still get taken off the pile, or they would all be waiting when the round starts
        if (IsRemote) Input.Read();
        return InputFlags.None;
    }

    /// <summary>
    /// Helper method to keep a knocked out player on the floor. It only kicks in once the hitstop of the hit that did it is over.
    /// </summary>
    private void StayDown()
    {
        // Does nothing if we are in it already, or if the move list doesn't have one (then they stay in hit_stun)
        ChangeToMove(MoveIds.KNOCKED_OUT);

        if (_wasKnockedOut) return;
        _wasKnockedOut = true;

        Player.TweenTint(KnockedOutTint, KNOCKED_OUT_FADE);
    }

    /* Moves */

    /// <summary>
    /// Helper method to play the frames of the move that are due on this tick.
    /// </summary>
    private void AdvanceMove()
    {
        _frameProgress += Player.Character.FrameRate / FightTicks.TICK_RATE;
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
        if (IsInControl) State.CurrentStatus = FighterStatus.Normal;
        CanCancel = true;

        // Handle stance reroutes || return to Idle automatically
        if (CurrentMove.FinishReroutes != null &&
            CurrentMove.FinishReroutes.TryGetValue(State.CurrentStance, out var rerouteId))
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
        if (IsInControl) State.CurrentStatus = FighterStatus.Normal;

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
        // Find a match as per the order of precedence defined in the movelist. A move we can't pay for is passed over, so A + B
        // without the meter for the special is a plain kick
        if (!MoveList.TryMatchInput(Inputs.Buffer, State.CurrentStance, Player.Meter, out var move, out var signature)) return;

        // A press only ever starts one move, held inputs (run, crouch, block) keep matching for as long as they are held
        if (move.Trigger != InputTrigger.Held)
        {
            Inputs.Buffer.Consume(signature);
        }

        // The meter is spent the moment the move is asked for, a reroute doesn't change what it cost
        Player.SpendMeter(move.MeterCost);

        // Check stance reroutes (hitting kick while in the air -> jumpkick)
        if (move.StanceReroutes != null &&
            move.StanceReroutes.TryGetValue(State.CurrentStance, out string? rerouteId))
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
        if (animName is null) return 1;

        return (uint)Math.Max(1, Player.GetFrameCount(animName));
    }

    /// <summary>
    /// Helper method to draw the frame the move is on. A move that outlasts its animation holds the last frame of it.
    /// </summary>
    private void ShowFrame()
    {
        // The sprite is on the animation the move last asked for, unless the character doesn't have it
        if (Player.FrameName != ActiveAnimation) return;

        Player.Frame = (int)ClampFrame(Playback.Shown);
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
    /// <param name="damage">How much the hit that did it took off us, for the combo counter.</param>
    /// <param name="knockdown">Whether the hit puts us on the floor once we come down from it, see <see cref="KnockDown"/>.</param>
    public void ApplyHitstun(int ticks, int damage = 0, bool knockdown = false)
    {
        State.ApplyHitstun(ticks, damage, knockdown);

        // Every hit restarts the move, so each hit of a combo is seen to land
        ChangeToMove(MoveIds.HIT_STUN, forceRestart: true);
    }

    /// <summary>
    /// Keeps us in our block for a number of ticks after it ate a hit. No move starts until it is over, and the block can't be dropped.
    /// </summary>
    public void ApplyBlockstun(int ticks) => State.ApplyBlockstun(ticks);

    /// <summary>
    /// Puts us on the floor. Nothing hits us down here, and once the knockdown runs out we get up with i-frames (the get_up move's).
    /// Called by the hit that does it on the ground, and by the landing for one that launched us first.
    /// </summary>
    public void KnockDown()
    {
        State.KnockDown(CombatRules.KNOCKDOWN_TICKS);

        // A move list without a floor to lie on makes do with the hitstun move
        ChangeToMove(MoveList.Moves.ContainsKey(MoveIds.KNOCKED_DOWN) ? MoveIds.KNOCKED_DOWN : MoveIds.HIT_STUN, forceRestart: true);
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

        State = new FighterState(Player);
        CanCancel = false;
        _hitstop = 0;
        _frameProgress = 0.0f;
        _wasKnockedOut = false;
        Movement.Reset();
        Taunt.Reset();

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
}
