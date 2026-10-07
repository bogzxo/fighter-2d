using System.Collections.Generic;
using System.Numerics;

namespace Fighter2D.Logic.Moves;

/// <summary>
/// One move out of a move file. Everything about it is data, the code only knows how to play it (see MovePlayback).
/// </summary>
public class FightingMove
{
    public string Id { get; init; } = MoveIds.IDLE;
    public int Damage { get; init; } = 0;

    // The shove whoever gets hit is given, X pushes them away from the attacker and Y launches them
    public Vector2 Knockback { get; init; } = Vector2.Zero;

    // How long whoever gets hit is in hitstun for, in frames of this move's own animation.
    // Below zero means the move didn't say, then it is worked out from its startup (see MoveFrameData)
    public float Hitstun { get; init; } = -1.0f;

    // The stances the move can be started from
    public Stance Stances { get; init; } = Fighter2D.Logic.Stance.Standing;

    // Any one of these starts the move, None means the move can only be reached through reroutes
    public InputFlags[] InputSignatures { get; init; } = [InputFlags.None];

    // How a signature has to be entered
    public InputTrigger Trigger { get; init; } = InputTrigger.Held;

    // The order the inputs are matched in, lower goes first. Negative for moves no input starts
    public int Priority { get; init; } = -1;

    // Whether the move can be cancelled into another one at all, its phases say from which frame
    public bool Cancellable { get; init; } = true;

    // Whether the player can walk during the move, and whether they can turn around during it
    public bool AllowsSteering { get; init; } = true;
    public bool AllowsTurning { get; init; } = true;

    // What the player is for as long as the move lasts (attacking, blocking, invulnerable), null leaves that alone
    public PlayerStatusType? Status { get; init; }

    // The stance the move puts the player in and the one it leaves them in after, null leaves that to the physics
    public Stance? Stance { get; init; }
    public Stance? StanceAfter { get; init; }

    // Whether the opponent gets a heads up the moment the move starts, which is what gives the dummy a chance to block
    public bool Warns { get; init; }

    // The move starts over once its last phase is done, for as long as this holds
    public MoveCondition Repeat { get; init; }

    // The frame by frame logic of the move
    public MovePhase[] Phases { get; init; } = [];

    // Does a different move instead depending on the stance the player is in when the input comes (kick in the air -> jump kick)
    public Dictionary<Stance, string>? StanceReroutes { get; init; }

    // Carries on into a different move once this one is finished, depending on the stance the player is in by then
    public Dictionary<Stance, string>? FinishReroutes { get; init; }

    // Startup, recovery and hitstun in ticks, worked out once the character's animations are known
    public MoveFrameData FrameData { get; internal set; }

    /// <summary>
    /// Whether the move sends whoever it hits flying, these are the ones that feel heavy.
    /// </summary>
    public bool HasKnockback => Knockback != Vector2.Zero;
}
