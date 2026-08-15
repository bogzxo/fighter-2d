using System.Collections.Generic;

namespace Fighter2D.Logic;

public class FrameRoutine
{
    private readonly IEnumerator<int> _routine;
    private int _waitFrames = 0;

    public bool IsFinished { get; private set; }

    public FrameRoutine(IEnumerator<int> routine)
    {
        _routine = routine;
    }

    /// <summary>
    /// Steps the routine forward by one frame. Call this in FixedUpdate.
    /// </summary>
    public int Tick()
    {
        if (IsFinished) return 0;

        if (_waitFrames > 0)
        {
            _waitFrames--;
            return _waitFrames;
        }

        if (_routine.MoveNext())
        {
            // The yielded integer is how many frames to wait before the next step
            _waitFrames = _routine.Current;
        }
        else
        {
            IsFinished = true;
        }
        return _waitFrames;
    }
}