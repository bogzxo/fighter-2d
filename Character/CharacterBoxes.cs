using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Bogz.Logging;
using Bogz.Logging.Loggers;

using Horizon.Rendering.Spriting;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fighter2D.Character;

/// <summary>
/// The boxes of one frame of animation, plus the outline of what is drawn on it (as line pieces of two points each).
/// Everything is a share of the frame where (0, 0) is its middle, x is the way the character faces and y is up.
/// </summary>
internal readonly record struct FrameBoxes(Box Hurtbox, Box Hitbox, Vector2[] Outline);

/// <summary>
/// The hurtbox and hitbox of a character for every frame of every animation, read straight off its sprite sheet.
/// Nobody has to draw boxes by hand, the art is the boxes (see <see cref="SpriteBoxTracer"/>).
/// </summary>
internal sealed class CharacterBoxes
{
    private const string SHEET_FILE = "spritesheet.png";

    // Sheets are big and every player of a character shares them, so each is only ever traced once
    private static readonly Dictionary<string, CharacterBoxes?> cache = [];

    private readonly Dictionary<string, FrameBoxes[]> _animations = [];

    /// <summary>
    /// Helper method to read the boxes of a character off its sprite sheet, null if the sheet can't be read.
    /// </summary>
    /// <param name="directory">The folder the sprite sheet is in.</param>
    /// <param name="animations">The animations of the sheet, which is how the frames are found on it.</param>
    public static CharacterBoxes? Load(string directory, SpriteSheetAnimationManager animations)
    {
        lock (cache)
        {
            if (cache.TryGetValue(directory, out var known)) return known;

            CharacterBoxes? read = null;
            try
            {
                string file = Path.Combine(directory, SHEET_FILE);
                if (File.Exists(file))
                {
                    using var image = Image.Load<Rgba32>(file);
                    read = new CharacterBoxes(image, animations);
                }
            }
            catch (Exception exception)
            {
                // A character without boxes still works, it just falls back to a dumb box in the middle
                ConcurrentLogger.Instance.Log(LogLevel.Error, $"Couldn't read the hitboxes of '{directory}': {exception.Message}");
            }

            return cache[directory] = read;
        }
    }

    private CharacterBoxes(Image<Rgba32> image, SpriteSheetAnimationManager animations)
    {
        foreach (var (name, animation) in animations.Animations)
        {
            int width = (int)animation.FirstFrame.Size.X, height = (int)animation.FirstFrame.Size.Y;
            if (width < 1 || height < 1) continue;

            var frames = new FrameBoxes[Math.Max(1, animation.Length)];
            for (int i = 0; i < frames.Length; i++)
            {
                // The frames of an animation sit next to each other on the sheet
                int left = ((int)animation.FirstFrame.Position.X + i) * width, top = (int)animation.FirstFrame.Position.Y * height;
                frames[i] = SpriteBoxTracer.Trace(image, left, top, width, height);
            }

            _animations[name] = frames;
        }
    }

    /// <summary>
    /// Helper method to get the boxes of a frame, false for an animation or a frame the character doesn't have.
    /// </summary>
    public bool TryGet(string animation, uint frame, out FrameBoxes boxes)
    {
        if (_animations.TryGetValue(animation, out var frames) && frames.Length > 0)
        {
            boxes = frames[Math.Min(frame, (uint)frames.Length - 1)];
            return boxes.Hurtbox.Max.X > boxes.Hurtbox.Min.X;
        }

        boxes = new FrameBoxes(default, default, []);
        return false;
    }
}
