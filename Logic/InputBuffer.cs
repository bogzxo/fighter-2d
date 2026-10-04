using Fighter2D.Logic.Moves;

namespace Fighter2D.Logic;

/// <summary>
/// Helper class which remembers the last few ticks of player input, this allows moves to be matched against
/// button presses (even ones made slightly too early) and double taps, rather than only what is held right now.
/// </summary>
public class InputBuffer
{
    private readonly (InputFlags Held, InputFlags Pressed)[] _ticks;
    private readonly int _bufferTicks, _doubleTapTicks;
    private int _newest = -1;
    private int _count = 0;

    /// <summary>
    /// Everything that was held down on the most recent tick.
    /// </summary>
    public InputFlags Held => _count > 0 ? _ticks[_newest].Held : InputFlags.None;

    /// <param name="bufferTicks">How many ticks a press stays valid for.</param>
    /// <param name="doubleTapTicks">The longest gap between the two presses of a double tap.</param>
    public InputBuffer(int bufferTicks, int doubleTapTicks)
    {
        _bufferTicks = bufferTicks;
        _doubleTapTicks = doubleTapTicks;

        // Enough room for a double tap whose second press is about to expire
        _ticks = new (InputFlags, InputFlags)[bufferTicks + doubleTapTicks + 1];
    }

    /// <summary>
    /// Saves the state of the player input for this tick/frame.
    /// </summary>
    public void Push(InputFlags held)
    {
        // A press is anything that is held now but wasn't on the last tick, the diff, whatever is already
        // held on the very first tick doesn't count (the button used to confirm the menu etc.)
        InputFlags pressed = _count > 0 ? held & ~Held : InputFlags.None;

        _newest = (_newest + 1) % _ticks.Length;
        _ticks[_newest] = (held, pressed);
        _count = Math.Min(_count + 1, _ticks.Length);
    }

    /// <summary>
    /// Tests if every button of the signature is held down right now.
    /// </summary>
    public bool IsHeld(InputFlags signature) => (Held & signature) == signature;

    /// <summary>
    /// Tests if the signature was pressed recently enough to still count.
    /// </summary>
    public bool WasPressed(InputFlags signature) => FindPress(signature, 0, _bufferTicks) >= 0;

    /// <summary>
    /// Tests if the signature was pressed twice in a row, the latest press being recent enough to still count.
    /// </summary>
    public bool WasDoubleTapped(InputFlags signature)
    {
        int secondTap = FindPress(signature, 0, _bufferTicks);
        return secondTap >= 0 && FindPress(signature, secondTap + 1, _doubleTapTicks) >= 0;
    }

    /// <summary>
    /// Tests the input against a move, as per the trigger of the move.
    /// </summary>
    /// <param name="signature">The input signature of the move which matched.</param>
    public bool TryMatch(FightingMove move, out InputFlags signature)
    {
        foreach (InputFlags candidate in move.InputSignatures)
        {
            // Moves without an input can only be reached through reroutes
            if (candidate == InputFlags.None) continue;

            bool matched = move.Trigger switch
            {
                InputTrigger.Held => IsHeld(candidate),
                InputTrigger.Pressed => WasPressed(candidate),
                InputTrigger.DoubleTap => WasDoubleTapped(candidate),
                _ => false
            };

            if (!matched) continue;

            signature = candidate;
            return true;
        }

        signature = InputFlags.None;
        return false;
    }

    /// <summary>
    /// Forgets the presses of a signature, called once they have started a move so they can't start another.
    /// </summary>
    public void Consume(InputFlags signature)
    {
        for (int i = 0; i < _ticks.Length; i++)
            _ticks[i].Pressed &= ~signature;
    }

    /// <summary>
    /// Helper method to find the most recent tick on which the signature was pressed.
    /// </summary>
    /// <returns>How many ticks ago the press happened, -1 if there was none in the window.</returns>
    private int FindPress(InputFlags signature, int fromTicksAgo, int windowTicks)
    {
        int end = Math.Min(fromTicksAgo + windowTicks, _count);

        for (int ticksAgo = fromTicksAgo; ticksAgo < end; ticksAgo++)
        {
            var (held, pressed) = _ticks[(_newest - ticksAgo + _ticks.Length) % _ticks.Length];

            // For signatures of several buttons (A + B) the press is the tick the last of them went down
            if ((pressed & signature) != InputFlags.None && (held & signature) == signature)
                return ticksAgo;
        }

        return -1;
    }
}
