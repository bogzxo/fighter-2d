using System;
using System.Collections.Generic;

using Fighter2D.Logic;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// One line of the input display, which is a set of buttons and how many ticks they were held for.
/// </summary>
internal readonly record struct InputLogEntry(InputFlags Held, int Ticks);

/// <summary>
/// The buttons a player has held lately. The buffer is what moves get matched against, the history is what gets sent to the other machine.
/// </summary>
internal sealed class PlayerInputs
{
    // How many ticks of history are kept, plenty for the few that every network message repeats
    private const int HISTORY = 32;

    // How many lines the input display remembers, and the highest number of ticks a line counts up to
    private const int LOG_LENGTH = 12;
    private const int LOG_MAX_TICKS = 99;

    private readonly InputFlags[] _history = new InputFlags[HISTORY];
    private readonly List<InputLogEntry> _log = [];

    /// <summary>
    /// What the player has held lately, newest first. Every change of buttons starts a new line. This is what the input display shows.
    /// </summary>
    public IReadOnlyList<InputLogEntry> Log => _log;

    public InputBuffer Buffer { get; } = new(PlayerConfig.INPUT_BUFFER_TICKS, PlayerConfig.DOUBLE_TAP_TICKS);

    /// <summary>
    /// Saves what was held on a tick.
    /// </summary>
    public void Push(uint tick, InputFlags held)
    {
        Buffer.Push(held);
        _history[tick % HISTORY] = held;
        WriteLog(held);
    }

    private void WriteLog(InputFlags held)
    {
        // Still holding the same thing, the newest line just counts up
        if (_log.Count > 0 && _log[0].Held == held)
        {
            _log[0] = _log[0] with { Ticks = Math.Min(LOG_MAX_TICKS, _log[0].Ticks + 1) };
            return;
        }

        _log.Insert(0, new InputLogEntry(held, 1));
        if (_log.Count > LOG_LENGTH) _log.RemoveAt(_log.Count - 1);
    }

    /// <summary>
    /// What was held on a tick, only the last few are remembered.
    /// </summary>
    public InputFlags At(uint tick) => _history[tick % HISTORY];

    /// <summary>
    /// Tests if the buttons are held down right now. Moves use this to know when to end (letting go of block etc.)
    /// </summary>
    public bool IsHeld(InputFlags buttons) => Buffer.IsHeld(buttons);

    /// <summary>
    /// The way the player is steering, -1 for left, 1 for right and 0 for neither.
    /// </summary>
    public float Steering => (IsHeld(InputFlags.DPadRight) ? 1 : 0) - (IsHeld(InputFlags.DPadLeft) ? 1 : 0);

    /// <summary>
    /// Lets go of everything, for when a round resets.
    /// </summary>
    public void Release()
    {
        Buffer.Push(InputFlags.None);
        _log.Clear();
    }
}
