using System;
using System.Numerics;

using Fighter2D.Character.Controllers;
using Fighter2D.Scenes;

namespace Fighter2D.Logic.Moves;

/// <summary>
/// Plays a move frame by frame the way its file describes it: one phase after the other, each with its animation, the frame it hits on,
/// the frame it can be cut short from and what it waits for. There is one of these for every player, whatever move they are in.
/// Where it is in the move is all it remembers (which phase, which frame of it), so it can be put anywhere in any move
/// from the outside: that is how the player of another machine is kept in step, see <see cref="Restore"/>.
/// </summary>
internal sealed class MovePlayback(PlayerController controller)
{
    // The effects a phase can ask for by name
    private const string EFFECT_JUMP = "jump";
    private const string EFFECT_ROLL = "roll";
    private const string EFFECT_HAZE = "haze";

    public FightingMove Move { get; private set; } = new() { Id = MoveIds.NONE };

    /// <summary>
    /// The phase of the move that is playing, and how many frames of it have been played since it last started over.
    /// </summary>
    public int Phase { get; private set; } = -1;
    public uint Frame { get; private set; }

    /// <summary>
    /// The frame of the animation there is to show: how many frames ago it started, which goes on counting through
    /// phases that have no animation of their own.
    /// </summary>
    public uint Shown { get; private set; }

    public bool IsFinished { get; private set; }

    // How many frames the phase lasts before it is over or starts again
    private uint _frames;

    // The frame that gets shown next, and how many frames of the phase have had the chance of an effect
    private uint _nextShown, _effectFrame;

    // Whether a phase is playing, and whether any of them has spent a frame since the move last started over
    private bool _inPhase, _tookTime;

    /// <summary>
    /// Starts a move and plays its first frame there and then, so there is no delay on the input.
    /// </summary>
    public void Begin(FightingMove move)
    {
        Move = move;
        Phase = -1;
        Frame = Shown = _frames = _nextShown = _effectFrame = 0;
        _inPhase = _tookTime = IsFinished = false;

        var state = controller.StateTracker;
        if (move.Status is { } status) state.CurrentStatus = status;
        if (move.Stance is { } stance) state.CurrentStance = stance;
        if (move.Warns) controller.Opponent.Controller.PrepareForHit();

        Step();
    }

    /// <summary>
    /// Plays the next frame of the move.
    /// </summary>
    /// <returns>False if there was none left to play: the move is over, and whatever comes after it is up to the controller.</returns>
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
                    // A phase ends on the frame what it waits on changes, and that frame goes to whatever comes next
                    if ((phase.While != MoveCondition.None && !Test(phase.While)) || (phase.Until != MoveCondition.None && Test(phase.Until)))
                    {
                        _inPhase = false;
                        continue;
                    }

                    Play(phase, (int)Frame);

                    Frame++;
                    Shown = _nextShown++;
                    _tookTime = true;
                    return true;
                }

                // A phase that loops starts over for as long as what it waits on holds
                if (phase.While != MoveCondition.None)
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

                // A phase that waits on something which isn't the case never starts (letting go of block before it is up)
                if (next.While != MoveCondition.None && !Test(next.While)) continue;

                Enter(next);
                continue;
            }

            // A move that starts over without any of its phases running would never let go of the frame
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

            // We update the stance change here so the controller knows the move let go of it (landed, stood up)
            if (Move.StanceAfter is { } after) controller.StateTracker.CurrentStance = after;

            IsFinished = true;
            return false;
        }
    }

    /// <summary>
    /// Puts the playback at a frame of a move without playing anything that leads up to it: nobody is pushed, hit or
    /// warned. This is for a player of another machine that has turned out to be somewhere else than we had them.
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

        // Whatever of the effects of the phase was due by now has been and gone
        _effectFrame = frame;

        controller.PlayAnimation(animation);
        Shown = shown;
        _nextShown = shown + 1;
    }

    private void Enter(MovePhase phase)
    {
        var player = controller.Player;

        if (phase.Impulse != Vector2.Zero)
        {
            // X is the way we are facing
            player.PhysicsBody.ApplyImpulse(new Vector2(player.Facing * phase.Impulse.X, phase.Impulse.Y));

            // Going up is the start of a new fall
            if (phase.Impulse.Y > 0) controller.StateTracker.ResetFallDuration();
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
            _nextShown = 0;
        }

        _frames = LengthOf(phase);
        Frame = 0;
    }

    private uint LengthOf(MovePhase phase) => phase.Frames > 0 ? phase.Frames : controller.GetAnimationLength(phase.Animation);

    /// <summary>
    /// Helper method for everything a phase does on one of its frames.
    /// </summary>
    private void Play(MovePhase phase, int frame)
    {
        if (frame == phase.Interrupt) controller.CanInterrupt = true;
        if (frame == phase.Lock) controller.CanInterrupt = false;
        if (phase.Status is { } status && frame == phase.StatusFrame) controller.StateTracker.CurrentStatus = status;

        // What a blow reaches is read off the frame that is drawn of it
        if (frame == phase.Hit) controller.TryHit(_nextShown);

        if (phase.Effect is not null && (phase.EffectFrames == 0 || _effectFrame < phase.EffectFrames))
        {
            PlayEffect(phase.Effect);
        }
        _effectFrame++;
    }

    private bool Test(MoveCondition condition) => condition switch
    {
        MoveCondition.Held => IsHeld(),
        MoveCondition.Stunned => controller.StateTracker.IsStunned,
        MoveCondition.Jumping => controller.StateTracker.CurrentStance == Stance.Jumping,
        MoveCondition.Falling => controller.StateTracker.FallDuration > 0,
        MoveCondition.Always => true,
        _ => false
    };

    /// <summary>
    /// Helper method to test if any of the inputs that start the move is (still) held down.
    /// </summary>
    private bool IsHeld()
    {
        foreach (InputFlags signature in Move.InputSignatures)
        {
            if (signature != InputFlags.None && controller.IsHeld(signature)) return true;
        }

        return false;
    }

    private void PlayEffect(string effect)
    {
        var player = controller.Player;

        switch (effect)
        {
            case EFFECT_JUMP:
                FightScene.Effects.Dust(player.FeetPosition, 0);
                FightScene.Effects.Shockwave(player.FeetPosition, 45, 110);
                break;

            case EFFECT_ROLL:
                // A trail of dust behind us. Every frame of it, so what stays underfoot gets all of them: each one has to be gentle
                FightScene.Effects.Dust(player.FeetPosition, -player.Facing, 8);
                FightScene.Effects.Shockwave(player.FeetPosition, 70, 45);
                break;

            case EFFECT_HAZE:
                FightScene.Effects.Haze(player.HeadPosition);
                break;
        }
    }

    /// <summary>
    /// Whether a phase can ask for an effect by this name, for telling whoever wrote the file when it can't.
    /// </summary>
    public static bool IsEffect(string effect) => effect is EFFECT_JUMP or EFFECT_ROLL or EFFECT_HAZE;
}
