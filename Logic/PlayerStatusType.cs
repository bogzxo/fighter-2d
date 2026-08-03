namespace Fighter2D.Logic;

using System;
using System.Collections.Generic;

using Horizon.HIDL.Runtime;

using Silk.NET.Input;

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
    Stunned,
    ComboTrapped
}

public struct MoveAnimation
{
    public string Name;
    public int HitFrame;
}

internal class FightingMove
{
    public string Name { get; set; } = string.Empty;
    public int Damage { get; set; }
    public MoveAnimation Animation { get; set; }
    public Stance Stances { get; set; }
    public Direction Directions { get; set; }
    //public ButtonName[] Bindings { get; set; } = [];
    public InputFlags InputSignature { get; set; }
    public bool UseAnyBindings { get; set; }
    public bool Interuptable { get; set; }
    public bool Loopable { get; set; }
    public bool DoubleTap { get; set; }
    public string? NextMove { get; set; }
    public string? ReleaseMove { get; set; }
    public Dictionary<Stance, string>? StanceReroutes { get; set; }
    public AnonymousFunctionValue? Callback { get; set; }
}