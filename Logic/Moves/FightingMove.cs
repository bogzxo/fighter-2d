using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fighter2D.Logic.Moves;

/// <summary>
/// What a phase of a move waits on, or keeps going for.
/// </summary>
public enum MoveCondition
{
    None,
    Held,      // one of the inputs of the move is held down
    Stunned,   // the player is stunned
    Jumping,   // the player is on the way up
    Falling,   // the player is on the way down
    Always
}

/// <summary>
/// One stretch of a move: an animation, and what happens on which of its frames.
/// A move is its phases one after the other, see Assets/data/fighting_moves.hor for how they are written down.
/// </summary>
public class MovePhase
{
    // The animation that plays, null carries on with the one of the phase before
    public string? Animation { get; init; }

    // How many frames the phase lasts, 0 for as long as its animation is
    public uint Frames { get; init; }

    // The frame a hit is attempted on, -1 for a phase that doesn't hit anybody
    public int Hit { get; init; } = -1;

    // The frame from which other moves may cut this one short, -1 if the phase doesn't change that
    public int Interrupt { get; init; } = -1;

    // The frame from which other moves can't cut this one short any more, -1 if the phase doesn't change that
    public int Lock { get; init; } = -1;

    // What the player becomes during the phase (guarding, invulnerable) and the frame of it they do, for a status that
    // doesn't hold from the very start of the move. It lasts until the move is over
    public PlayerStatusType? Status { get; init; }
    public int StatusFrame { get; init; }

    // The phase starts over for as long as this holds, and ends on the frame it stops holding
    public MoveCondition While { get; init; }

    // The phase ends on the frame this starts holding
    public MoveCondition Until { get; init; }

    // A push the player gets as the phase starts, X is the way they are facing and Y is up
    public Vector2 Impulse { get; init; }

    // Something for the eye (see MovePlayback for the ones there are), and for how many frames it keeps coming: 0 is every frame
    public string? Effect { get; init; }
    public uint EffectFrames { get; init; } = 1;
}

/// <summary>
/// How long the parts of a move take, in ticks of the fight (see PlayerConfig.TICK_RATE) rather than in frames of its
/// animation: characters animate at different rates, and what a hit comes to has to be the same for all of them.
/// It is worked out once the animations of the character are known, see <see cref="MoveList.Bake"/>.
/// </summary>
/// <param name="Startup">How long after the move starts its hit is thrown, 0 for a move that doesn't hit.</param>
/// <param name="Recovery">How long the move goes on for after its hit was thrown.</param>
/// <param name="HitStun">How long whoever it lands on is stunned for.</param>
public readonly record struct MoveFrameData(int Startup, int Recovery, int HitStun)
{
    /// <summary>
    /// How many ticks sooner than whoever got hit the attacker is free again, below zero if it is the other way round.
    /// </summary>
    public int Advantage => HitStun - Recovery;

    /// <summary>
    /// Helper method to work out the frame data of a move.
    /// </summary>
    /// <param name="animationLength">How many frames an animation of the character has.</param>
    /// <param name="frameRate">How many frames of animation a second the character plays at.</param>
    public static MoveFrameData Of(FightingMove move, Func<string?, uint> animationLength, float frameRate)
    {
        float ticksPerFrame = Character.Controllers.PlayerConfig.TICK_RATE / MathF.Max(1.0f, frameRate);

        // A phase that loops counts once, how often it comes round isn't known until it is played
        uint total = 0;
        int hitFrame = -1;

        foreach (MovePhase phase in move.Phases)
        {
            uint frames = phase.Frames > 0 ? phase.Frames : Math.Max(1, animationLength(phase.Animation));

            if (hitFrame < 0 && phase.Hit >= 0) hitFrame = (int)total + phase.Hit;
            total += frames;
        }

        if (hitFrame < 0) return default;

        float startup = hitFrame * ticksPerFrame;
        float recovery = (total - hitFrame) * ticksPerFrame;

        // A move that says how long it stuns for is taken at its word, in frames of its own animation. One that doesn't
        // stuns for as long as it took to wind up: the longer the wait, the more whoever lands it gets out of it
        float stun = move.Stun >= 0
            ? move.Stun * ticksPerFrame
            : recovery + MathF.Min(Combat.MAX_ADVANTAGE, startup * Combat.WINDUP_REWARD - Combat.BASE_DISADVANTAGE);

        return new MoveFrameData(
            (int)MathF.Round(startup),
            (int)MathF.Round(recovery),
            Math.Max(Combat.MIN_HIT_STUN, (int)MathF.Round(stun)));
    }
}

public class FightingMove
{
    public string Id { get; init; } = MoveIds.IDLE;
    public int Damage { get; init; } = 0;

    // The impulse given to whoever gets hit, X pushes them away from the attacker and Y launches them into the air
    public Vector2 Knockback { get; init; } = Vector2.Zero;

    // How long whoever gets hit is stunned for, in frames of the animation of this move. Below zero leaves it to how
    // long the move takes to wind up, see MoveFrameData
    public float Stun { get; init; } = -1.0f;

    public Stance Stances { get; init; } = Fighter2D.Logic.Stance.Standing;

    // Any one of these starts the move, None means the move can only be reached through reroutes
    public InputFlags[] InputSignatures { get; init; } = [InputFlags.None];

    // How a signature has to be entered
    public InputTrigger Trigger { get; init; } = InputTrigger.Held;

    // Where the move stands in the order the inputs are matched in, lower goes first. Negative for moves no input starts
    public int Priority { get; init; } = -1;

    // Whether other moves may cut this one short, the phases decide when exactly through their interrupt frame
    public bool Interruptible { get; init; } = true;

    // Whether the player can walk during the move, and whether they can turn around during it
    public bool AllowsSteering { get; init; } = true;
    public bool AllowsTurning { get; init; } = true;

    // What the player is for as long as the move lasts (attacking, guarding, invulnerable), null leaves that alone
    public PlayerStatusType? Status { get; init; }

    // The stance the move puts the player in, and the one it leaves them in when it is over. Null leaves that to the physics
    public Stance? Stance { get; init; }
    public Stance? StanceAfter { get; init; }

    // Whether the other player is told the moment the move starts, which is what gives them a chance to block
    public bool Warns { get; init; }

    // The move starts over once its last phase is done, for as long as this holds
    public MoveCondition Repeat { get; init; }

    // The frame-by-frame logic of the move
    public MovePhase[] Phases { get; init; } = [];

    // Reroutes the input to a different move depending on the player's stance
    public Dictionary<Stance, string>? StanceReroutes { get; init; }

    // Continues into a different move once this one has finished, depending on the player's stance
    public Dictionary<Stance, string>? FinishReroutes { get; init; }

    // How long the move takes and how long it stuns for, known once the character it belongs to is
    public MoveFrameData FrameData { get; internal set; }
}
