using System;
using System.Numerics;

namespace Fighter2D.Simulation;

/// <summary>
/// The brain of a player. It reads the buttons of its input, matches them against the move list and plays the move they start.
/// There is one of these per player and they are all the same, who presses the buttons is up to the <see cref="IPlayerInput"/> it is given.
/// Everything runs on fixed ticks (see FightTicks.TICK_RATE) no matter how fast the game is drawn.
/// </summary>
internal sealed class PlayerController(IPlayerInput input)
{
    // The most ticks one update catches up on. After a hitch the fight carries on from where it is rather than fast forwarding
    private const int MAX_TICKS_PER_UPDATE = 8;

    private static readonly FightingMove NoMove = new() { Id = MoveIds.NONE };

    // What somebody who got knocked out is dimmed to, and how long (in seconds) that takes
    private static readonly Vector4 KnockedOutTint = new(0.62f, 0.62f, 0.72f, 1.0f);
    private const float KNOCKED_OUT_FADE = 0.5f;

    public IPlayerInput Input { get; } = input;
    public PlayerInputs Inputs { get; } = new();
    public IFighter Player { get; private set; } = null!;

    /// <summary>
    /// The player's body in the world, what the moves push about and what shows the frames.
    /// </summary>
    public IFighterBody Body => Player.Body;
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
    /// The fight we are in, the player's, see <see cref="FightContext"/>.
    /// </summary>
    public FightContext Fight => Player.Fight;

    /// <summary>
    /// The player we are fighting against.
    /// </summary>
    public IFighter Opponent => Fight.OpponentOf(Player);

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

    /// <summary>
    /// Whether the hit of the move we are in has landed (or been blocked), which opens its hit cancels.
    /// </summary>
    public bool HitLanded { get; private set; }

    // How many more active frames the hit of the move we are in has to land on, 0 once it landed or ran out
    private int _activeLeft;

    // How many frames of recovery the move we are in has played since its last active frame, see IsMovementCancelOpen
    private int _recoveryFrames;

    // What may start while the move we are in is still going, made once in Attach rather than a delegate a tick, and the
    // same two with only the presses in them, see TryMatch
    private Func<FightingMove, bool> _allowedAfterHit = null!, _pressedAfterHit = null!;
    private static readonly Func<FightingMove, bool> PressedOnly = IsPressed;

    /// <summary>
    /// Which active frame of its move the hitbox is on, 0 on the hit frame, -1 when there's no hitbox out. It stays out
    /// for all of the active frames even after it landed, it just doesn't land twice, which is what the hitbox view shows.
    /// </summary>
    public int ActiveFrame { get; private set; } = -1;

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
    public bool CanStartMove => IsInControl && _hitstop == 0 && _whiffLeft == 0 && !State.IsInBlockstun && (CanCancel && !IsWhiffing || IsHitCancelOpen || IsMovementCancelOpen);

    /// <summary>
    /// Whether the hit of the move we are in landed (or was blocked) and enough of its recovery has gone by to leave it for a
    /// move that doesn't attack, a jump after whoever we just launched, a step in, the block. See CombatRules.HIT_MOVEMENT_CANCEL.
    /// A whiff gets none of this, it plays out.
    /// </summary>
    public bool IsMovementCancelOpen => HitLanded && ActiveFrame < 0 && _recoveryFrames >= CombatRules.HIT_MOVEMENT_CANCEL;

    /// <summary>
    /// The stance a press is matched in. On the ground the direction held with the button decides, down and a kick is the
    /// crouching kick whether or not we were crouching already and nothing held is the standing one, so a crouching move is
    /// one input and not a stance to get into first. In the air it is wherever we are. A block that ate a hit keeps its stance.
    /// </summary>
    private Stance MatchStance => State.IsGrounded && !State.IsInBlockstun
        ? (Inputs.IsHeld(InputFlags.DPadDown) ? Stance.Crouching : Stance.Standing)
        : State.CurrentStance;

    /// <summary>
    /// Whether we are frozen in the hitlag of a hit, ours or one we took. The body doesn't move, see PlayerMovement.
    /// </summary>
    public bool IsFrozen => _hitstop > 0;

    /// <summary>
    /// Whether the move we are in threw its hit and missed, and says that costs (whiff_lag). Its cancel frames don't count.
    /// </summary>
    public bool IsWhiffing => _whiffed && CurrentMove.WhiffLag > 0;

    /// <summary>
    /// The tick our block last went up on, a hit that lands within a few ticks of it is parried.
    /// </summary>
    public uint BlockStartedAt { get; private set; }

    /// <summary>
    /// Whether the hit of the move we are in landed and it lets some moves cancel it from here, see <see cref="FightingMove.HitCancel"/>.
    /// </summary>
    public bool IsHitCancelOpen => HitLanded && CurrentMove.HitCancel is not null;

    /// <summary>
    /// Whether the player can steer right now, and whether they can turn around. How fast they go on the ground and in
    /// the air is up to the move they are in (walk and drift), see PlayerMovement. A block can't be turned around, you
    /// have to let go of it first.
    /// </summary>
    public bool CanSteer => IsInControl;
    public bool CanTurn => IsInControl && CurrentMove.AllowsTurning && !IsBlocking;

    // How much of the next tick of the fight has gone by, in thousandths of one. Counted in whole numbers rather than
    // by adding up seconds: a sixtieth of a second made of two halves of one in floats comes up a hair short every so
    // often, and the fight stands still for an update and then takes two ticks in one, which looks like arse
    private const int TICK_PARTS = 1000;
    private long _tickParts;

    // How many more ticks a hit that just landed holds us frozen for
    private int _hitstop;

    // Whether the rounds had us held still on the last tick
    private bool _frozen;

    // What the body was doing when a hitlag froze it, and what it does once the hitlag is over instead (a launch, a shove),
    // with whether the direction we hold then gets a say in it (DI). See ApplyHitstop and ReleaseHitstop
    private Vector2 _resumeVelocity;
    private Vector2? _pendingLaunch;
    private bool _pendingInfluence;

    // Where the directions pressed during a hitlag want to shove us (SDI), and how many ticks until the next one counts
    private Vector2 _sdi;
    private int _sdiCooldown;

    // The tick block was last pressed on the way down from a knockdown, 0 for none, see TryTech
    private uint _techAt;

    // Whether the hit of the move we are in came out and missed, whether the whiff lag has been tacked on, and how much is left
    private bool _whiffed, _whiffHeld;
    private int _whiffLeft;

    // Whether we were blocking on the tick before, for spotting the tick the block goes up on
    private bool _wasBlocking;

    // The moves of ours that landed lately, the newest at _staleNext - 1, for stale move negation (see StaleScale)
    private readonly string?[] _stale = new string?[CombatRules.STALE_STEPS.Length];
    private int _staleNext;

    // The press that started the move we are in, when, and where that move is in the list, for turning it into a two
    // button move if the other button comes down a moment later. None when a held input or the game started the move
    private InputFlags _chordBy;
    private uint _chordAt;
    private int _chordPriority;

    // Whether we were knocked out on the last tick, for spotting the tick we go down on
    private bool _wasKnockedOut;

    /// <summary>
    /// Called by the player once it has its body and its moves, this is where the controller comes to life.
    /// </summary>
    public void Attach(IFighter player)
    {
        Player = player;
        MoveList = Player.MoveList;
        State = new FighterState(Player);
        Playback = new MovePlayback(this);
        Movement = new PlayerMovement(this);
        Taunt = new VictoryTaunt(this);
        _allowedAfterHit = AllowedAfterHit;
        _pressedAfterHit = IsPressedAfterHit;

        Input.Attach(this);
        ChangeToMove(MoveIds.IDLE, forceRestart: true);
    }

    /// <summary>
    /// Called every update by the player, plays the ticks of the fight that are due.
    /// </summary>
    public void Update(float dt)
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

    /// <summary>
    /// Called every physics step by the player, see <see cref="PlayerMovement.UpdatePhysics"/>.
    /// </summary>
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

        // Update the physics side of the state, crouching, falling and grounded
        State.UpdatePhysicsState(FightTicks.TICK_TIME);
        WatchBlockAndTech();

        if (_hitstop > 0)
        {
            // Hitstop freezes the move and the hitstun both, and whoever got hit gets to wriggle
            _hitstop--;
            TakeSdi();
            if (_hitstop == 0) ReleaseHitstop();
        }
        else
        {
            // Somebody who is knocked out never gets out of hitstun, for everybody else the hitstun move (or lying on the floor)
            // lasts exactly as long as the hitstun does. The blockstun runs down alongside, the block move lets go when it is out
            State.TickBlockstun();
            State.TickLandingLag();

            if (IsKnockedOut) StayDown();
            else if (State.TickHitstun() && CurrentMove.Id is MoveIds.HIT_STUN or MoveIds.KNOCKED_DOWN) FinishMove();

            AdvanceMove();
        }

        Movement.CheckLanding();
        if (!frozen) Movement.CheckFastFall();

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
        else TryChord();
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

        Body.TintTo(KnockedOutTint, KNOCKED_OUT_FADE);
    }

    /* Moves */

    /// <summary>
    /// Helper method to play the frames of the move that are due on this tick.
    /// </summary>
    private void AdvanceMove()
    {
        // A move that missed and says it costs holds its last frame a while longer
        if (_whiffLeft > 0)
        {
            if (--_whiffLeft == 0) FinishMove();
            ShowFrame();
            return;
        }

        // A move that is over hands its frame to whatever comes after it
        if (!Playback.Step())
        {
            if (IsWhiffing && !_whiffHeld)
            {
                _whiffHeld = true;
                _whiffLeft = CurrentMove.WhiffLag;
            }
            else FinishMove();
        }

        ShowFrame();
    }

    /// <summary>
    /// Helper method to note the tick our block goes up on (for parries) and a press of block on the way down from a
    /// knockdown (for teching).
    /// </summary>
    private void WatchBlockAndTech()
    {
        bool blocking = IsBlocking;
        if (blocking && !_wasBlocking) BlockStartedAt = Tick;
        _wasBlocking = blocking;

        if (!State.IsInHitstun || !State.KnockdownPending || State.IsGrounded) return;
        if ((Inputs.Buffer.PressedNow & InputFlags.RightBumper) == 0) return;

        // Mashing it gets you nothing, a press that wasn't in time locks it out for a while
        if (_techAt == 0 || Tick - _techAt > CombatRules.TECH_LOCKOUT) _techAt = Tick;
    }

    /// <summary>
    /// Called as we land from a knockdown. If block was pressed in time we get up on the spot, or roll the way we hold,
    /// instead of lying there.
    /// </summary>
    /// <returns>Whether we teched.</returns>
    public bool TryTech()
    {
        if (_techAt == 0 || Tick - _techAt > CombatRules.TECH_WINDOW) return false;
        _techAt = 0;

        State.Tech();

        float direction = Inputs.Steering;
        if (direction != 0 && MoveList.Moves.ContainsKey(MoveIds.TECH_ROLL))
        {
            // The roll goes the way it is facing, so face the way it was asked for
            Body.Flipped = direction < 0;
            ChangeToMove(MoveIds.TECH_ROLL, forceRestart: true);
        }
        else ChangeToMove(MoveList.Moves.ContainsKey(MoveIds.TECH) ? MoveIds.TECH : MoveIds.GET_UP, forceRestart: true);

        return true;
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
            // A move that an input holds up (crouch_hold after a crouching attack) is only carried on with while that input
            // is still held. Let go of during the attack they are stood up by the time it ends, and a press that was buffered
            // is the standing move. It used to pass through crouch_hold for a tick, still crouching, and crouch kick all over again
            if (MoveList.TryGetMove(rerouteId, out var next) && IsLetGoOf(next))
            {
                if (next.StanceAfter is { } after) State.CurrentStance = after;
                ChangeToMove(MoveIds.IDLE, forceRestart: true);
                return;
            }

            ChangeToMove(rerouteId, forceRestart: true);
        }
        else
        {
            // Automatically return to Idle when an attack/move finishes! (I always make her finish)
            ChangeToMove(MoveIds.IDLE, forceRestart: true);
        }
    }

    /// <summary>
    /// Helper method to test if a move is one that a held input keeps going and that input isn't held.
    /// </summary>
    private bool IsLetGoOf(FightingMove move) =>
        move.Trigger == InputTrigger.Held
        && Array.Exists(move.InputSignatures, signature => signature != InputFlags.None)
        && !Inputs.Buffer.TryMatch(move, out _);

    public void ChangeToMove(string newMoveId, bool forceRestart = false)
    {
        // Test if we are attempting to do the same move we already are and only allow it if the restart flag is set.
        if (!forceRestart && CurrentMove.Id == newMoveId) return;

        // Test if the move we are attempting to change to exists, the moves are whatever the move files of the character say
        if (!MoveList.TryGetMove(newMoveId, out var newMove)) return;

        CanCancel = false;
        HitThrown = HitLanded = false;
        _chordBy = InputFlags.None;
        _whiffed = _whiffHeld = false;
        _whiffLeft = 0;
        _activeLeft = 0;
        _recoveryFrames = 0;
        ActiveFrame = -1;

        // The statuses set by a move end with it, even when it was cancelled (rolling -> kick)
        if (IsInControl) State.CurrentStatus = FighterStatus.Normal;

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
        // Past the cancel frame anything goes, before it only what the move cancels into once its hit has landed
        Func<FightingMove, bool>? allowed = CanCancel && !IsWhiffing ? null : _allowedAfterHit;
        if (!TryMatch(allowed, out var move, out var signature, out Stance stance)) return;

        StartMatched(move, signature, stance);
    }

    /// <summary>
    /// Helper method for what may start while the move we are in is still going. What it says it cancels into once its hit
    /// landed (hit_cancel), and a bit later a move that doesn't attack (see IsMovementCancelOpen).
    /// </summary>
    private bool AllowedAfterHit(FightingMove move) =>
        (IsHitCancelOpen && CurrentMove.HitCancelsInto(move)) || (IsMovementCancelOpen && move.IsMovement);

    private static bool IsPressed(FightingMove move) => move.Trigger != InputTrigger.Held;
    private bool IsPressedAfterHit(FightingMove move) => IsPressed(move) && AllowedAfterHit(move);

    /// <summary>
    /// Helper method to turn the move a press just started into a two button move that press is half of, when the other
    /// button comes down a moment later (A, then B, is A + B). Only for the first few ticks and only before anything came out.
    /// </summary>
    private void TryChord()
    {
        if (_chordBy == InputFlags.None || Tick - _chordAt > CombatRules.CHORD_TICKS || HitThrown || !IsInControl || _hitstop > 0) return;

        if (!TryMatch(IsChordOf, out var move, out var signature, out Stance stance)) return;

        StartMatched(move, signature, stance);
    }

    /// <summary>
    /// Helper method to find the move the buttons are asking for. A press is matched in the stance the direction held with it
    /// asks for (see <see cref="MatchStance"/>), a held input (the crouch, the run, the block) in the stance the body is in.
    /// Down and a button that has no crouching version comes out standing, over the crouch the direction alone would be, so
    /// every button does something whatever is held with it.
    /// </summary>
    /// <param name="stance">The stance the move was matched in, which is the one its reroutes go by.</param>
    private bool TryMatch(Func<FightingMove, bool>? allowed, out FightingMove move, out InputFlags signature, out Stance stance)
    {
        stance = MatchStance;
        Stance body = State.CurrentStance;
        bool matched = MoveList.TryMatchInput(Inputs.Buffer, stance, body, Player.Meter, State.AirJumpsLeft, allowed, out move, out signature);

        // Only on the ground with down held, and only when what was found is the direction itself (or nothing), a press wins
        if (stance != Stance.Crouching || !State.IsGrounded || (matched && IsPressed(move))) return matched;

        Func<FightingMove, bool>? pressed = allowed is null ? PressedOnly : ReferenceEquals(allowed, _allowedAfterHit) ? _pressedAfterHit : null;
        if (pressed is null || !MoveList.TryMatchInput(Inputs.Buffer, Stance.Standing, body, Player.Meter, State.AirJumpsLeft, pressed, out FightingMove standing, out InputFlags pressedWith))
            return matched;

        move = standing;
        signature = pressedWith;
        stance = Stance.Standing;
        return true;
    }

    /// <summary>
    /// Helper method to test if a move has a two button input the press that started the move we are in is half of, and
    /// comes before that move in the list.
    /// </summary>
    private bool IsChordOf(FightingMove move)
    {
        if (move.Priority >= _chordPriority) return false;

        foreach (InputFlags signature in move.InputSignatures)
        {
            if (signature != _chordBy && (signature & _chordBy) == _chordBy) return true;
        }

        return false;
    }

    /// <summary>
    /// Helper method to start the move the buttons matched, paying for it and going wherever its stance sends it.
    /// </summary>
    /// <param name="stance">The stance the move was matched in, which decides its reroute (kick with down held is the low kick).</param>
    private void StartMatched(FightingMove move, InputFlags signature, Stance stance)
    {
        // A press only ever starts one move, held inputs (run, crouch, block) keep matching for as long as they are held
        if (move.Trigger != InputTrigger.Held)
        {
            Inputs.Buffer.Consume(signature);
        }
        else if (CurrentMove.Trigger == InputTrigger.Held && CurrentMove.Id != move.Id && Array.IndexOf(CurrentMove.InputSignatures, signature) >= 0)
        {
            // The input that is held is the one keeping the move we are in going, so it doesn't get to start a different one. Down held
            // after a crouching attack has us in crouch_hold, and without this the crouch move (matched first, it comes before it in
            // the list) would start over and duck all over again
            return;
        }

        // The meter is spent the moment the move is asked for, a reroute doesn't change what it cost
        Player.SpendMeter(move.MeterCost);

        // Check stance reroutes (hitting kick while in the air -> jumpkick, kick with down held -> low kick)
        if (move.StanceReroutes != null &&
            move.StanceReroutes.TryGetValue(stance, out string? rerouteId))
        {
            ChangeToMove(rerouteId);
        }
        else
        {
            ChangeToMove(move.Id);
        }

        // A press that started a move can still grow into a two button move for a moment, see TryChord
        bool pressed = move.Trigger != InputTrigger.Held;
        _chordBy = pressed ? signature : InputFlags.None;
        _chordAt = Tick;
        _chordPriority = move.Priority;
    }

    /* Animation */

    /// <summary>
    /// Helper method for the moves to change the animation that is playing.
    /// </summary>
    public void PlayAnimation(string animName)
    {
        ActiveAnimation = animName;
        Body.PlayAnimation(ActiveAnimation);
    }

    /// <summary>
    /// Helper method for the moves to find out how many frames an animation has, 1 for one the character doesn't have.
    /// </summary>
    public uint GetAnimationLength(string? animName)
    {
        if (animName is null) return 1;

        return (uint)Math.Max(1, Body.FrameCount(animName));
    }

    /// <summary>
    /// Helper method to draw the frame the move is on. A move that outlasts its animation holds the last frame of it.
    /// </summary>
    private void ShowFrame()
    {
        // The sprite is on the animation the move last asked for, unless the character doesn't have it
        if (Body.ShownAnimation != ActiveAnimation) return;

        Body.ShowFrame((int)ClampFrame(Playback.Shown));
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
        int active = Math.Max(1, CurrentMove.ActiveFrames);
        ActiveFrame = 0;
        HitLanded = HitResolver.TryLand(this, ClampFrame(frame), active <= 1);
        _activeLeft = HitLanded ? 0 : active - 1;
        _whiffed = !HitLanded && active <= 1;
    }

    /// <summary>
    /// Called by the move on every frame after its hit frame. The hitbox is still out on its active frames and gets
    /// another go at landing on every one of them, a whiff is only a whiff on the last.
    /// </summary>
    public void KeepHitting(uint frame)
    {
        if (ActiveFrame >= 0) ActiveFrame = ActiveFrame + 1 < Math.Max(1, CurrentMove.ActiveFrames) ? ActiveFrame + 1 : -1;
        if (HitThrown && ActiveFrame < 0) _recoveryFrames++;
        if (_activeLeft <= 0) return;

        _activeLeft--;
        if (HitResolver.TryLand(this, ClampFrame(frame), _activeLeft == 0))
        {
            HitLanded = true;
            _activeLeft = 0;
        }
        else if (_activeLeft == 0) _whiffed = true;
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
    /// <param name="gravityShare">How much of our gravity we fall with until it is over.</param>
    /// <param name="juggle">Whether we were already up in the air in hitstun when it landed.</param>
    public void ApplyHitstun(int ticks, int damage = 0, bool knockdown = false, float gravityShare = 1.0f, bool juggle = false)
    {
        State.ApplyHitstun(ticks, damage, knockdown, gravityShare, juggle);
        Movement.UpdateGravity();

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
    /// Freezes the player on the frame they are on, and where they are, for a number of ticks. Whatever they were doing
    /// carries on once it is over, unless the hit queued a launch or a shove (QueueLaunch), which goes then.
    /// </summary>
    public void ApplyHitstop(int ticks)
    {
        if (ticks <= 0)
        {
            if (_hitstop == 0) ReleaseHitstop();
            return;
        }

        if (_hitstop == 0) _resumeVelocity = Body.Velocity;
        _hitstop = Math.Max(_hitstop, ticks);
    }

    /// <summary>
    /// What the body does once the hitlag is over, a launch or a shove. A launch (influenced) can be turned by the direction
    /// held as the hitlag ends, which is DI.
    /// </summary>
    public void QueueLaunch(Vector2 velocity, bool influenced)
    {
        _pendingLaunch = velocity;
        _pendingInfluence = influenced;
    }

    /// <summary>
    /// Helper method for the end of a hitlag, the body goes again, with the launch it was queued if there is one.
    /// </summary>
    private void ReleaseHitstop()
    {
        Vector2 velocity = _pendingLaunch ?? _resumeVelocity;
        if (_pendingLaunch is not null && _pendingInfluence && State.IsInHitstun) velocity = Influence(velocity);

        _pendingLaunch = null;
        _resumeVelocity = Vector2.Zero;
        _sdi = Vector2.Zero;
        Body.SetVelocity(velocity);
        Movement.UpdateGravity();
    }

    /// <summary>
    /// Helper method to turn a launch by the direction held, up to DI_MAX_ANGLE, most from a direction square on to it.
    /// </summary>
    private Vector2 Influence(Vector2 launch)
    {
        Vector2 held = HeldDirection();
        float speed = launch.Length();
        if (held == Vector2.Zero || speed < 1.0f) return launch;

        Vector2 across = new Vector2(-launch.Y, launch.X) / speed;
        float turn = Vector2.Dot(Vector2.Normalize(held), across) * CombatRules.DI_MAX_ANGLE;
        float cos = MathF.Cos(turn), sin = MathF.Sin(turn);
        return new Vector2(launch.X * cos - launch.Y * sin, launch.X * sin + launch.Y * cos);
    }

    /// <summary>
    /// Helper method for SDI, every direction pressed during a hitlag we took shoves us that way, one every few ticks.
    /// </summary>
    private void TakeSdi()
    {
        if (!State.IsInHitstun) return;
        if (_sdiCooldown > 0)
        {
            _sdiCooldown--;
            return;
        }

        InputFlags pressed = Inputs.Buffer.PressedNow;
        float x = ((pressed & InputFlags.DPadRight) != 0 ? 1 : 0) - ((pressed & InputFlags.DPadLeft) != 0 ? 1 : 0);
        float y = ((pressed & InputFlags.DPadUp) != 0 ? 1 : 0) - (!State.IsGrounded && (pressed & InputFlags.DPadDown) != 0 ? 1 : 0);
        if (x == 0 && y == 0) return;

        _sdi += Vector2.Normalize(new Vector2(x, y)) * CombatRules.SDI_DISTANCE;
        _sdiCooldown = CombatRules.SDI_INTERVAL;
    }

    /// <summary>
    /// How far the SDI of this tick shoves us, taken by the physics once.
    /// </summary>
    public Vector2 TakeSdiShove()
    {
        Vector2 shove = _sdi;
        _sdi = Vector2.Zero;
        return shove;
    }

    private Vector2 HeldDirection() => new(
        Inputs.Steering,
        (Inputs.IsHeld(InputFlags.DPadUp) ? 1 : 0) - (Inputs.IsHeld(InputFlags.DPadDown) ? 1 : 0));

    /// <summary>
    /// How much of its damage a move of ours does, less for every time it landed lately (Smash's stale move negation).
    /// </summary>
    public float StaleScale(FightingMove move)
    {
        float scale = 1.0f;
        int length = _stale.Length;
        for (int place = 0; place < length; place++)
        {
            if (_stale[(_staleNext - 1 - place + length) % length] == move.Id) scale -= CombatRules.STALE_STEPS[place];
        }
        return scale;
    }

    /// <summary>
    /// Remembers a move of ours that landed, for stale move negation.
    /// </summary>
    public void RememberLanded(FightingMove move)
    {
        _stale[_staleNext] = move.Id;
        _staleNext = (_staleNext + 1) % _stale.Length;
    }

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
        _wasKnockedOut = false;
        _pendingLaunch = null;
        _sdi = Vector2.Zero;
        _techAt = 0;
        Array.Clear(_stale);
        _staleNext = 0;
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
        ShowFrame();
    }
}
