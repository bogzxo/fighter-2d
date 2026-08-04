using System;
using System.Collections.Generic;

using Fighter2D.Logic.Moves;

using Silk.NET.Input;

namespace Fighter2D.Logic.Moves;

public class FightingMove
{
    public MoveId Id { get; init; } = MoveId.Idle;
    public int Damage { get; init; } = 0;
    public string AnimationName { get; init; } = "idle";

    public Stance Stances { get; init; } = Stance.Standing;
    public InputFlags InputSignature { get; init; } = InputFlags.None;

    public bool Interruptible { get; init; } = true;
    public bool DoubleTap { get; init; } = false;

    // contains the frame-by-frame logic for this move
    public Func<IEnumerator<int>>? RoutineFactory { get; set; }

    // Reroutes the input to a different move depending on the player's stance
    public Dictionary<Stance, MoveId>? StanceReroutes { get; set; }
}