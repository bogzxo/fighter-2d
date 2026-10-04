using System.Collections.Generic;

namespace Fighter2D.Logic;

/// <summary>
/// Helper class providing a method to step through the move routines to facilitate pacing of frame perfect move logic.
/// </summary>
public class FrameRoutine(IEnumerator<uint> routine)
{
    private uint _waitFrames = 0;
    private uint _currentFrame = 0;

    public bool IsFinished { get; private set; }

    public void ResetFrame()
    {
        _currentFrame = 0;
    }

    /// <summary>
    /// Runs the routine up to its first wait without spending a frame, so a move sets itself up the moment it is chosen.
    /// </summary>
    public void Start()
    {
        if (routine.MoveNext())
        {
            _waitFrames = (uint)routine.Current;
        }
        else
        {
            IsFinished = true;
        }
    }

    public uint Tick()
    {
        if (IsFinished) return _currentFrame;

        while (_waitFrames == 0)
        {
            if (routine.MoveNext())
            {
                _waitFrames = (uint)routine.Current;
            }
            else
            {
                IsFinished = true;
                return _currentFrame > 0 ? _currentFrame - 1 : 0;
            }
        }

        _waitFrames--;
        return _currentFrame++;
    }
}