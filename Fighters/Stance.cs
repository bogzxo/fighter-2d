using System;

namespace Fighter2D.Logic;

/// <summary>
/// How a player is standing (or not standing). Moves say which stances they can be started from, so it is a flag.
/// </summary>
[Flags]
public enum Stance
{
    None = 0,
    Standing = 1 << 0,
    Crouching = 1 << 1,
    Jumping = 1 << 2,
    Falling = 1 << 3
}
