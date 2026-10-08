using System;
using System.Numerics;

namespace Fighter2D.Moves;

/// <summary>
/// One stretch of a move, which is an animation and what happens on which of its frames.
/// A move is its phases played one after the other. See Assets/data/fighting_moves.hor for how they are written down.
/// </summary>
public class MovePhase
{
    // The animation that plays, null carries on with the animation of the phase before
    public string? Animation { get; init; }

    // The frame of the animation the phase starts on, for an animation that only gets going a few frames in
    public uint Start { get; init; }

    // How many frames of the move every frame of the animation is held for, 0 if the phase doesn't say (then it is 1 for a phase
    // with an animation of its own, and whatever the phase before had for one without). An animation that was drawn for
    // half the frame rate of the character is held for 2
    public uint Hold { get; init; }

    // How many frames the phase lasts, 0 for as long as what is left of its animation
    public uint Frames { get; init; }

    /// <summary>
    /// How many frames the phase lasts, given how long its animation is.
    /// </summary>
    public uint LengthFor(uint animationLength) =>
        Frames > 0 ? Frames : Math.Max(1, animationLength > Start ? animationLength - Start : 1) * Math.Max(1, Hold);

    // The frame the hit comes out on, -1 for a phase that doesn't hit anybody
    public int HitFrame { get; init; } = -1;

    // The frame from which the move can be cancelled into another one, -1 if the phase doesn't change that
    public int CancelFrame { get; init; } = -1;

    // The frame from which the player is committed and can't cancel any more, -1 if the phase doesn't change that
    public int CommitFrame { get; init; } = -1;

    // The status the player takes on during the phase and the frame it kicks in on, for a status that doesn't hold from the very start (block)
    public FighterStatus? Status { get; init; }
    public int StatusFrame { get; init; }

    // The phase starts over for as long as this holds and ends on the frame it stops holding
    public MoveCondition LoopWhile { get; init; }

    // The phase ends on the frame this starts holding
    public MoveCondition Until { get; init; }

    // A shove the player gets as the phase starts, X is the way they are facing and Y is up
    public Vector2 Impulse { get; init; }

    // A bit of eye candy by name (see MoveEffects) and how many frames it keeps coming for, 0 is every frame
    public string? Effect { get; init; }
    public uint EffectFrames { get; init; } = 1;
}
