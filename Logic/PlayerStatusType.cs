using System;

namespace Fighter2D.Logic;

[Flags]
public enum Stance
{
    None = 0,
    Standing = 1 << 0,
    Crouching = 1 << 1,
    Jumping = 1 << 2,
    Falling = 1 << 3
}

[Flags]
public enum Direction
{
    None = 0,
    Any = 1 << 0,
    Forward = 1 << 1,
    Backward = 1 << 2,
    Up = 1 << 3,
    Down = 1 << 4
}

public enum PlayerStatusType
{
    Normal,
    Attacking,    // for move coroutines
    Guarding,     // for block coroutine
    Invulnerable, // for dodge roll coroutine
    Stunned,
    ComboTrapped
}