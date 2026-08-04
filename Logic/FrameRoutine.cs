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
    public void Tick()
    {
        if (IsFinished) return;

        if (_waitFrames > 0)
        {
            _waitFrames--;
            return;
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
    }
}