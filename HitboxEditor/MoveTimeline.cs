using Fighter2D.Moves;

namespace Fighter2D.HitboxEditor;

/// <summary>What a frame of a move is, as far as its hit goes.</summary>
internal enum FrameKind
{
    /// <summary>Before the hit, winding up. Getting hit now is a counter hit.</summary>
    Startup,

    /// <summary>The hitbox is out. The first of these is the hit frame.</summary>
    Active,

    /// <summary>After the hitbox has gone, wide open. Getting hit now is a punish.</summary>
    Recovery,

    /// <summary>A move that doesn't hit anybody, walking, blocking, lying on the floor.</summary>
    Neutral
}

/// <summary>One frame of a move, laid out the way the game plays it.</summary>
/// <param name="Phase">Which phase of the move it's in, and what that phase is called.</param>
/// <param name="Animation">The animation showing, and <paramref name="Shown"/> which frame of it.</param>
/// <param name="Loops">Whether its phase goes round again while something holds (a block held up), it's shown once.</param>
internal readonly record struct TimelineFrame(int Index, int Phase, string PhaseName, string Animation, int Shown, FrameKind Kind, bool IsHitFrame, bool Loops);

/// <summary>
/// Lays a move out frame by frame, which animation frame shows on every frame of the move, where its hit frame is and
/// which frames are startup, active and recovery. Worked out the way MovePlayback plays a move, phase after phase,
/// animations held for their hold and carried on through phases that have none of their own, so the frame the editor
/// shows a box on is the frame the fight tests it on.
/// </summary>
internal static class MoveTimeline
{
    /// <param name="frameCount">How many frames an animation of the character has.</param>
    /// <param name="active">How many frames the hitbox stays out for from the hit frame.</param>
    public static TimelineFrame[] Of(FightingMove move, Func<string, int> frameCount, int active)
    {
        var frames = new List<TimelineFrame>();
        string animation = "idle";
        uint shownStart = 0, steps = 0, hold = 1;
        int hit = -1;

        for (int p = 0; p < move.Phases.Length; p++)
        {
            MovePhase phase = move.Phases[p];

            if (phase.Animation is { } own)
            {
                animation = own;
                shownStart = phase.Start;
                steps = 0;
                hold = Math.Max(1, phase.Hold);
            }
            else if (phase.Hold > 0 && phase.Hold != hold)
            {
                // The same animation carries on at another pace, from the frame it's on
                shownStart += steps / hold;
                steps = 0;
                hold = phase.Hold;
            }

            // A phase without an animation of its own is a frame long unless it says, the way the controller counts it
            uint length = phase.LengthFor(phase.Animation is null ? 1u : (uint)Math.Max(1, frameCount(phase.Animation)));
            int last = Math.Max(1, frameCount(animation)) - 1;
            bool loops = phase.LoopWhile != MoveCondition.None;
            string name = phase.Name.Length > 0 ? phase.Name : $"phase {p + 1}";

            for (uint f = 0; f < length; f++)
            {
                bool isHit = hit < 0 && f == phase.HitFrame;
                if (isHit) hit = frames.Count;

                int shown = (int)Math.Min(shownStart + steps / hold, (uint)last);
                frames.Add(new TimelineFrame(frames.Count, p, name, animation, shown, FrameKind.Neutral, isHit, loops));
                steps++;
            }
        }

        if (hit < 0) return [.. frames];

        int end = hit + Math.Max(1, active);
        for (int i = 0; i < frames.Count; i++)
        {
            FrameKind kind = i < hit ? FrameKind.Startup : i < end ? FrameKind.Active : FrameKind.Recovery;
            frames[i] = frames[i] with { Kind = kind };
        }

        return [.. frames];
    }

    /// <summary>Where the hit frame is, -1 for a move that doesn't hit.</summary>
    public static int HitFrame(TimelineFrame[] frames) => Array.FindIndex(frames, frame => frame.IsHitFrame);
}
