namespace Fighter2D.Tests.Support;

/// <summary>
/// Buttons by the tick, held from one tick up to (not including) another. Read once a tick by the controller, which
/// is how it counts the ticks.
/// </summary>
internal sealed class ScriptedInput : IPlayerInput
{
    private readonly List<(int From, int Until, InputFlags Held)> _steps = [];
    private int _tick;

    /// <summary>The tick the last read was for, which is the tick the controller is on.</summary>
    public int Tick => _tick;

    public ScriptedInput Hold(int from, int until, InputFlags held)
    {
        _steps.Add((from, until, held));
        return this;
    }

    /// <summary>Presses something for one tick.</summary>
    public ScriptedInput Tap(int at, InputFlags pressed) => Hold(at, at + 1, pressed);

    public InputFlags Read()
    {
        _tick++;
        InputFlags held = InputFlags.None;
        foreach (var (from, until, flags) in _steps)
        {
            if (_tick >= from && _tick < until) held |= flags;
        }
        return held;
    }
}
