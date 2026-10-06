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
    Attacking,    // for moves that throw a blow
    Guarding,     // for the block, which only covers the side the player is facing
    Invulnerable, // for the dodge roll
    Stunned       // the controls are taken away, for as many ticks as the blow that did it says (see Combat)
}