using System;
using System.Numerics;

namespace Fighter2D.Simulation;

// The moves of the controller, how one plays out and ends, and which one the buttons start. What is pressed is matched
// against the move list in its order (TryMatch), the stance held picks the version of a move (MatchStance), and a
// second button a few ticks after the first turns the move it started into the two button one (TryChord).
internal sealed partial class PlayerController
{
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
}
