using System.Collections.Generic;

namespace Fighter2D.Logic;

/// <summary>
/// Helper class providing a method to step through the move routines to facilitate pacing of frame perfect move logic.
/// </summary>
public class FrameRoutine(IEnumerator<int> routine)
{
    private int _waitFrames = 0;

    public bool IsFinished { get; private set; }

    /// <summary>
    /// Steps the routine forward by one frame.
    /// </summary>
    public int Tick()
    {
        if (IsFinished) return 0;

        if (_waitFrames > 0)
        {
            _waitFrames--;
            return _waitFrames;
        }

        if (routine.MoveNext())
        {
            // The yielded integer is how many frames to wait before the next step
            _waitFrames = routine.Current;
        }
        else
        {
            IsFinished = true;
        }
        return _waitFrames;
    }
}