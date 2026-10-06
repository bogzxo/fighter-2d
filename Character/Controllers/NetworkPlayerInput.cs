using System;
using System.Collections.Generic;

using Fighter2D.Logic;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Input for a network player. It plays back the buttons they held on every tick of their fight, in the order they held them.
/// The controller makes of those buttons what it makes of anybody's.
/// </summary>
internal class NetworkPlayerInput : IPlayerInput
{
    // How many ticks can be waiting before we start skipping the boring ones to catch up
    private const int MAX_BACKLOG = 3;

    // More than this waiting means the other machine hung for a moment and then sent the lot, so most of it gets binned
    private const int MAX_QUEUE = 30;

    // What has come in and hasn't been played yet, oldest first
    private readonly List<(uint Tick, InputFlags Held)> _queue = [];

    private InputFlags _held;
    private uint _newest;
    private bool _started;

    public string Name => "Network";
    public bool IsRemote => true;

    /// <summary>
    /// The tick of the other machine whose buttons were played last.
    /// </summary>
    public uint Tick { get; private set; }

    /// <summary>
    /// Called for every input message of the other machine. Each one repeats the last few ticks, so a lost message is covered by the next.
    /// </summary>
    /// <param name="newestTick">The tick the first entry was held on, each entry after it is one tick older.</param>
    public void Receive(uint newestTick, ReadOnlySpan<InputFlags> newestFirst)
    {
        for (int i = newestFirst.Length - 1; i >= 0; i--)
        {
            uint tick = newestTick - (uint)i;

            // Only take what we haven't heard yet. The difference is signed so it survives the counter wrapping around
            if (_started && (int)(tick - _newest) <= 0) continue;

            _queue.Add((tick, newestFirst[i]));
            _newest = tick;
            _started = true;
        }

        if (_queue.Count > MAX_QUEUE) _queue.RemoveRange(0, _queue.Count - MAX_BACKLOG);
    }

    public InputFlags Read()
    {
        // Running behind. A tick where nothing changed can be skipped without losing a press, since a press is a change
        while (_queue.Count > MAX_BACKLOG && _queue[0].Held == _queue[1].Held)
        {
            _queue.RemoveAt(0);
        }

        // Nothing new to go by, so they keep holding whatever they held
        if (_queue.Count == 0) return _held;

        (Tick, _held) = _queue[0];
        _queue.RemoveAt(0);

        return _held;
    }
}
