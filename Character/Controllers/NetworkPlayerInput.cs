using System;
using System.Collections.Generic;

using Fighter2D.Logic;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Input for a player of another machine: the buttons they held on every tick of their fight, pressed here in the order
/// they were pressed there. The controller makes of them what it makes of anybody's buttons.
/// </summary>
internal class NetworkPlayerInput : IPlayerInput
{
    // How many ticks may be waiting before the ones that say nothing new are skipped to catch up, and how many before
    // the lot is thrown out (the other machine stood still for a moment and sent it all at once)
    private const int MAX_BACKLOG = 3;
    private const int MAX_QUEUE = 30;

    // What has come in and hasn't been pressed yet, oldest first
    private readonly List<(uint Tick, InputFlags Held)> _queue = [];

    private InputFlags _held;
    private uint _newest;
    private bool _started;

    public string Name => "Network";
    public bool IsRemote => true;

    /// <summary>
    /// The tick of the other machine whose buttons were pressed last.
    /// </summary>
    public uint Tick { get; private set; }

    /// <summary>
    /// This method is called for every message of buttons the other machine sends. Each of them repeats the last few
    /// ticks, so one that gets lost is made up for by the next.
    /// </summary>
    /// <param name="newestTick">The tick the first of the buttons were held on, each one after it is a tick older.</param>
    public void Receive(uint newestTick, ReadOnlySpan<InputFlags> newestFirst)
    {
        for (int i = newestFirst.Length - 1; i >= 0; i--)
        {
            uint tick = newestTick - (uint)i;

            // Only what we haven't heard yet (the difference is taken as a signed one so it survives the counter wrapping)
            if (_started && (int)(tick - _newest) <= 0) continue;

            _queue.Add((tick, newestFirst[i]));
            _newest = tick;
            _started = true;
        }

        if (_queue.Count > MAX_QUEUE) _queue.RemoveRange(0, _queue.Count - MAX_BACKLOG);
    }

    public InputFlags Read()
    {
        // Behind: a tick on which nothing changed can go without anything being lost, a press is a change
        while (_queue.Count > MAX_BACKLOG && _queue[0].Held == _queue[1].Held)
        {
            _queue.RemoveAt(0);
        }

        // With nothing new to go by they carry on holding what they held
        if (_queue.Count == 0) return _held;

        (Tick, _held) = _queue[0];
        _queue.RemoveAt(0);

        return _held;
    }
}
