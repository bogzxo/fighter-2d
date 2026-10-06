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

    // Something for the eye (see MoveRoutine for the ones there are), and for how many frames it keeps coming: 0 is every frame
    public string? Effect { get; init; }
    public uint EffectFrames { get; init; } = 1;
}

public class FightingMove
{
    public string Id { get; init; } = MoveIds.IDLE;
    public int Damage { get; init; } = 0;

    // The impulse given to whoever gets hit, X pushes them away from the attacker and Y launches them into the air
    public Vector2 Knockback { get; init; } = Vector2.Zero;

    // How long (in seconds) whoever gets hit loses control for
    public float StunDuration { get; init; } = 0.0f;

    public Stance Stances { get; init; } = Fighter2D.Logic.Stance.Standing;

    // Any one of these starts the move, None means the move can only be reached through reroutes
    public InputFlags[] InputSignatures { get; init; } = [InputFlags.None];

    // How a signature has to be entered
    public InputTrigger Trigger { get; init; } = InputTrigger.Held;

    // Where the move stands in the order the inputs are matched in, lower goes first. Negative for moves no input starts
    public int Priority { get; init; } = -1;

    // Whether other moves may cut this one short, the phases decide when exactly through their interrupt frame
    public bool Interruptible { get; init; } = true;

    // Whether the player can walk and turn around during the move
    public bool AllowsSteering { get; init; } = true;

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
}
