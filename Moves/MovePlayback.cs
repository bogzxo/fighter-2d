using System;
using System.Numerics;

namespace Fighter2D.Moves;

/// <summary>
/// Plays a move frame by frame the way its file describes it, one phase after the other.
/// All it remembers is where it is in the move (which phase, which frame), so it can be dropped onto any frame of any move from outside.
/// That is how a network player gets yanked back in step, see <see cref="Restore"/>.
/// </summary>
internal sealed class MovePlayback(PlayerController controller)
{
    public FightingMove Move { get; private set; } = new() { Id = MoveIds.NONE };

    /// <summary>
    /// The phase of the move that is playing, and how many frames of it have been played since it last started over.
    /// </summary>
    public int Phase { get; private set; } = -1;
    public uint Frame { get; private set; }

    /// <summary>
    /// The frame of the animation to draw. It counts on through phases that have no animation of their own.
    /// </summary>
    public uint Shown { get; private set; }

    public bool IsFinished { get; private set; }

    // How many frames the phase lasts before it is over or loops
    private uint _frames;

    // The frame of the animation the playing started on, how many frames of the move have been played of it since and how
    // many of those every frame of the animation is held for. Which frame gets drawn next comes out of the three
    private uint _shownStart, _shownSteps, _hold = 1;

    // How many frames of the phase have had their go at an effect
    private uint _effectFrame;

    private uint NextShown => _shownStart + _shownSteps / _hold;

    // Whether a phase is playing, and whether any phase has spent a frame since the move last started over
    private bool _inPhase, _tookTime;

    /// <summary>
    /// Starts a move and plays its first frame right away so there is no delay on the input.
    /// </summary>
    public void Begin(FightingMove move)
    {
        Move = move;
        Phase = -1;
        Frame = Shown = _frames = _shownStart = _shownSteps = _effectFrame = 0;
        _hold = 1;
        _inPhase = _tookTime = IsFinished = false;

        var state = controller.State;
        if (move.Status is { } status) state.CurrentStatus = status;
        if (move.Stance is { } stance) state.CurrentStance = stance;
        if (move.Warns) controller.Opponent.Controller.OnOpponentAttack();

        Step();
    }

    /// <summary>
    /// Plays the next frame of the move.
    /// </summary>
    /// <returns>False if there was no frame left to play, what comes after the move is up to the controller.</returns>
    public bool Step()
    {
        if (IsFinished) return false;

        MovePhase[] phases = Move.Phases;

        while (true)
        {
            if (_inPhase)
            {
                MovePhase phase = phases[Phase];

                if (Frame < _frames)
                {
                    // A phase ends the moment what it waits on changes, and that frame goes to whatever comes next
                    if (ShouldLeave(phase))
                    {
                        _inPhase = false;
                        continue;
                    }

                    PlayFrame(phase, (int)Frame);

                    Frame++;
                    Shown = NextShown;
                    _shownSteps++;
                    _tookTime = true;
                    return true;
                }

                // A looping phase goes round again for as long as its condition holds
                if (phase.LoopWhile != MoveCondition.None)
                {
                    StartOver(phase);
                    continue;
                }

                _inPhase = false;
                continue;
            }

            if (Phase + 1 < phases.Length)
            {
                MovePhase next = phases[++Phase];

                // A phase whose loop condition already fails never starts (letting go of block before it is up)
                if (next.LoopWhile != MoveCondition.None && !Test(next.LoopWhile)) continue;

                Enter(next);
                continue;
            }

            // If no phase ran at all we still burn a frame, otherwise a repeating move would spin here forever
            if (!_tookTime)
            {
                _tookTime = true;
                return true;
            }

            if (Move.Repeat != MoveCondition.None && Test(Move.Repeat))
            {
                Phase = -1;
                _tookTime = false;
                continue;
            }

            // Set the stance here so the controller knows the move let go of it (landed, stood up)
            if (Move.StanceAfter is { } after) controller.State.CurrentStance = after;

            IsFinished = true;
            return false;
        }
    }

    /// <summary>
    /// Drops the playback onto a frame of a move without playing anything that leads up to it, so nobody gets shoved, hit or warned.
    /// This is for a network player that turned out to be somewhere else than we had them.
    /// </summary>
    /// <param name="animation">The animation that is showing there, and <paramref name="shown"/> the frame of it.</param>
    public void Restore(FightingMove move, int phase, uint frame, string animation, uint shown)
    {
        Move = move;
        IsFinished = false;
        _tookTime = true;

        Phase = Math.Clamp(phase, -1, move.Phases.Length - 1);
        _inPhase = Phase >= 0;
        _frames = _inPhase ? LengthOf(move.Phases[Phase]) : 0;
        Frame = Math.Min(frame, _frames);

        // Whatever effects were due by now have been and gone
        _effectFrame = frame;

        controller.PlayAnimation(animation);
        Shown = shown;

        // Carries on from the frame after, at the pace of the phase it was dropped into
        _hold = _inPhase ? Math.Max(1, move.Phases[Phase].Hold) : 1;
        _shownStart = shown + 1;
        _shownSteps = 0;
    }

    private bool ShouldLeave(MovePhase phase) =>
        (phase.LoopWhile != MoveCondition.None && !Test(phase.LoopWhile)) || (phase.Until != MoveCondition.None && Test(phase.Until));

    private void Enter(MovePhase phase)
    {
        var player = controller.Player;

        if (phase.Impulse != Vector2.Zero)
        {
            // X is the way we are facing
            player.PhysicsBody.ApplyImpulse(new Vector2(player.Facing * phase.Impulse.X, phase.Impulse.Y));

            // Going up is the start of a new fall
            if (phase.Impulse.Y > 0) controller.State.ResetFallDuration();
        }

        _inPhase = true;
        _effectFrame = 0;
        StartOver(phase);
    }

    private void StartOver(MovePhase phase)
    {
        if (phase.Animation is not null)
        {
            controller.PlayAnimation(phase.Animation);
            _shownStart = phase.Start;
            _shownSteps = 0;
            _hold = Math.Max(1, phase.Hold);
        }
        else if (phase.Hold > 0 && phase.Hold != _hold)
        {
            // The same animation carries on at another pace, from the frame it is on
            _shownStart = NextShown;
            _shownSteps = 0;
            _hold = phase.Hold;
        }

        _frames = LengthOf(phase);
        Frame = 0;
    }

    private uint LengthOf(MovePhase phase) => phase.LengthFor(controller.GetAnimationLength(phase.Animation));

    /// <summary>
    /// Helper method for everything a phase does on one of its frames.
    /// </summary>
    private void PlayFrame(MovePhase phase, int frame)
    {
        if (frame == phase.CancelFrame) controller.CanCancel = true;
        if (frame == phase.CommitFrame) controller.CanCancel = false;
        if (phase.Status is { } status && frame == phase.StatusFrame) controller.State.CurrentStatus = status;

        // The hitbox is read off the frame that is being drawn
        if (frame == phase.HitFrame) controller.ThrowHit(NextShown);

        if (phase.Effect is not null && (phase.EffectFrames == 0 || _effectFrame < phase.EffectFrames))
        {
            MoveEffects.Play(phase.Effect, controller.Player);
        }
        _effectFrame++;
    }

    private bool Test(MoveCondition condition) => condition switch
    {
        MoveCondition.Held => IsHeld(),
        MoveCondition.Hitstun => controller.State.IsInHitstun,
        MoveCondition.Jumping => controller.State.CurrentStance == Stance.Jumping,
        MoveCondition.Falling => controller.State.FallDuration > 0,
        MoveCondition.Always => true,
        _ => false
    };

    /// <summary>
    /// Helper method to test if any of the inputs that start the move is (still) held down.
    /// A block that just ate a hit counts as held for as long as the blockstun lasts, whatever the thumb is doing.
    /// </summary>
    private bool IsHeld()
    {
        if (Move.Status == FighterStatus.Blocking && controller.State.IsInBlockstun) return true;

        // Somebody walking over to gloat is walking, whatever their thumbs are doing
        if (Move.Id == MoveIds.RUN && controller.Taunt.IsWalking) return true;

        foreach (InputFlags signature in Move.InputSignatures)
        {
            if (signature != InputFlags.None && controller.Inputs.IsHeld(signature)) return true;
        }

        return false;
    }
}
