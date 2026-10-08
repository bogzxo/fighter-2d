using System;
using System.Collections.Generic;
using System.Numerics;

using Bogz.Logging;

using Horizon.Rendering.Spriting;

namespace Fighter2D.Fighters;

/// <summary>
/// The boxes of one frame of animation, plus the outline of what is drawn on it (as line pieces of two points each).
/// Everything is a share of the frame where (0, 0) is its middle, x is the way the character faces and y is up.
/// </summary>
/// <param name="Hurtbox">The box around everything that is drawn, which is where the character can be hit.</param>
/// <param name="Hitbox">The limb that sticks out in front, which is what the character hits with.</param>
/// <param name="Body">The bulk of the character without the limbs that stick out, which is what stands on the map and bumps into it.</param>
internal readonly record struct FrameBoxes(Box Hurtbox, Box Hitbox, Vector2[] Outline, Box Body = default);

/// <summary>
/// The hurtbox and hitbox of a character for every frame of every animation, read straight off its art.
/// Nobody has to draw boxes by hand, the art is the boxes (see <see cref="SpriteBoxTracer"/>).
/// An animation is traced the first time it is asked for, a character has a lot of them that a fight never shows.
/// </summary>
internal sealed class CharacterBoxes(SpriteSheetDefinition sprites)
{
    private static readonly FrameBoxes NoBoxes = new(default, default, []);

    // An animation that couldn't be traced is in here with no frames, so it is only tried once
    private readonly Dictionary<string, FrameBoxes[]> _animations = [];

    /// <summary>
    /// Traces every frame of an animation, unless that has been done already. Does nothing for one the character doesn't have.
    /// </summary>
    public void Trace(string animation) => FramesOf(animation);

    /// <summary>
    /// Helper method to get the boxes of a frame, false for an animation or a frame the character doesn't have.
    /// </summary>
    public bool TryGet(string animation, uint frame, out FrameBoxes boxes)
    {
        FrameBoxes[] frames = FramesOf(animation);
        if (frames.Length > 0)
        {
            boxes = frames[Math.Min(frame, (uint)frames.Length - 1)];
            return boxes.Hurtbox.Max.X > boxes.Hurtbox.Min.X;
        }

        boxes = NoBoxes;
        return false;
    }

    private FrameBoxes[] FramesOf(string animation)
    {
        lock (_animations)
        {
            if (_animations.TryGetValue(animation, out var known)) return known;

            FrameBoxes[] traced = [];
            try
            {
                if (sprites.TryGetSprite(animation, null, out SpriteSource source)) traced = TraceFrames(source);
            }
            catch (Exception exception)
            {
                // A character without boxes still works, it just falls back to a dumb box in the middle
                Log.Error($"Couldn't read the hitboxes of '{animation}' ({sprites.Path}): {exception.Message}");
            }

            return _animations[animation] = traced;
        }
    }

    private static FrameBoxes[] TraceFrames(in SpriteSource source)
    {
        var frames = new FrameBoxes[Math.Max(1, source.Frames)];

        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = source.ReadFrame(i) is { } pixels
                ? SpriteBoxTracer.Trace(pixels.Data, pixels.Width, pixels.Height)
                : NoBoxes;
        }

        return frames;
    }
}
