using System;
using System.Numerics;

namespace Fighter2D.Data;

/// <summary>
/// The jump a phase does as it starts, see <see cref="MovePhase.Jump"/>.
/// </summary>
public enum JumpKind
{
    None,
    Full,    // as high as the character's jump_height
    Short,   // as high as its short_hop_height
    Hop,     // full if the input of the move is still held as the phase starts, short if it was let go of during the squat
    Double   // as high as its double_jump_height, one of its jumps in the air
}

/// <summary>
/// One stretch of a move, which is an animation and what happens on which of its frames.
/// A move is its phases played one after the other. See Assets/data/fighting_moves.hor for how they are written down.
/// Every frame here is a frame of the fight, 60 a second, the art plays along at the character's own frame rate.
/// </summary>
public class MovePhase
{
    // What the move file calls the phase (windup, swing, recovery), for whoever shows the move to somebody
    public string Name { get; init; } = string.Empty;

    // The animation that plays, null carries on with the animation of the phase before
    public string? Animation { get; init; }

    // The frame of the art the phase starts on, for an animation that only gets going a few frames in
    public uint Start { get; init; }

    // How many frames the phase lasts, 0 for as long as what is left of its art takes at the character's frame rate
    public uint Frames { get; init; }

    // Whether the rest of the art is spread evenly over the frames of the phase rather than played at the character's
    // frame rate, Rivals of Aether's windows. Only means something with frames
    public bool Stretch { get; init; }

    // How fast the art plays against the character's frame rate, 0.5 for art drawn at half of it (or anything that is
    // only stood around in and would be a blur at full speed)
    public float ArtSpeed { get; init; } = 1.0f;

    /// <summary>
    /// How many frames of the fight the phase lasts, given how many frames its art has and how fast the character plays them.
    /// </summary>
    /// <param name="artFrames">How many frames its animation has, 1 for a phase that carries on with the one before.</param>
    public uint LengthFor(uint artFrames, float frameRate)
    {
        if (Frames > 0) return Frames;

        uint left = artFrames > Start ? artFrames - Start : 1;
        return (uint)Math.Max(1, MathF.Ceiling(left * FightTicks.TICK_RATE / MathF.Max(1.0f, frameRate * ArtSpeed)));
    }

    // The frame the hit comes out on, -1 for a phase that doesn't hit anybody
    public int HitFrame { get; init; } = -1;

    // How many frames the hitbox stays out for from the hit frame, 0 if the phase doesn't say
    public int Active { get; init; }

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

    // A shove the player gets as the phase starts, in units a second, X is the way they are facing and Y is up.
    // There is no drag any more so it is the speed they get, on the ground the traction takes it off again
    public Vector2 Impulse { get; init; }

    // The jump the player does as the phase starts, by the heights of their character
    public JumpKind Jump { get; init; }

    // A bit of eye candy by name (see MoveEffects) and how many frames it keeps coming for, 0 is every frame
    public string? Effect { get; init; }
    public uint EffectFrames { get; init; } = 1;
}
