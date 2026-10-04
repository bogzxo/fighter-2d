using System;
using System.Collections.Generic;
using System.Numerics;

using Fighter2D.Logic.Moves;

using Silk.NET.Input;

namespace Fighter2D.Logic.Moves;

public class FightingMove
{
    public MoveId Id { get; init; } = MoveId.Idle;
    public int Damage { get; init; } = 0;

    // The impulse given to whoever gets hit, X pushes them away from the attacker and Y launches them into the air
    public Vector2 Knockback { get; init; } = Vector2.Zero;

    // How long (in seconds) whoever gets hit loses control for
    public float StunDuration { get; init; } = 0.0f;

    public Stance Stances { get; init; } = Stance.Standing;

    // Any one of these starts the move, None means the move can only be reached through reroutes
    public InputFlags[] InputSignatures { get; init; } = [InputFlags.None];

    // How a signature has to be entered
    public InputTrigger Trigger { get; init; } = InputTrigger.Held;

    // Whether other moves may cut this one short, the routine decides when exactly through CanInterrupt
    public bool Interruptible { get; init; } = true;

    // Whether the player can walk and turn around during the move
    public bool AllowsSteering { get; init; } = true;

    // contains the frame-by-frame logic for this move
    public Func<IEnumerator<uint>>? RoutineFactory { get; set; }

    // Reroutes the input to a different move depending on the player's stance
    public Dictionary<Stance, MoveId>? StanceReroutes { get; set; }

    // Continues into a different move once this one has finished, depending on the player's stance
    public Dictionary<Stance, MoveId>? FinishReroutes { get; set; }
}
