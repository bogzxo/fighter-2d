using Horizon.HIDL.Runtime;

using Silk.NET.Input;

namespace CumInstinctDuel.Logic;

public readonly struct FightingMove
{
    public readonly string Name { get; init; }
    public readonly ButtonName[] Bindings { get; init; }
    public readonly Direction Directions { get; init; }
    public readonly Stance Stances { get; init; }
    public readonly int Damage { get; init; }
    public readonly MoveAnimation Animation { get; init; }
    public readonly bool UseAnyBindings { get; init; }
    public readonly bool Interuptable { get; init; }
    public readonly AnonymousFunctionValue? Callback { get; init; }
    public readonly bool Loopable { get; init; } // <-- Add this property
}
