using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Horizon.Core;
using Horizon.Rendering.Spriting;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fighter2D.Character;

/// <summary>
/// A box of the world, for testing what a blow reaches against what there is to be hit.
/// </summary>
internal readonly record struct Box(Vector2 Min, Vector2 Max)
{
    public Vector2 Center => (Min + Max) * 0.5f;

    /// <summary>
    /// Helper method to find out whether two boxes share any room, and where the middle of what they share is.
    /// </summary>
    public bool Overlaps(in Box other, out Vector2 middle)
    {
        Vector2 min = Vector2.Max(Min, other.Min), max = Vector2.Min(Max, other.Max);
        middle = (min + max) * 0.5f;

        return min.X < max.X && min.Y < max.Y;
    }
}

/// <summary>
/// Where a character can be hit and what it hits with, for every frame of every one of its animations, and the
/// outline of what is drawn of it on each.
internal sealed class CharacterBoxes
{
    private const string SHEET_FILE = "spritesheet.png";

    private const byte SOLID = 32;                // How much alpha a pixel needs to count as part of the character
    private const int BODY_SMOOTHING = 2;         // How many columns to either side one is averaged with, a braid or a gap is not the body
    private const float BODY_SHARE = 0.5f;        // How much a column has to hold against the fullest one to still be the body
    private const int LIMB_PIXELS = 12;           // Fewer pixels than this past the body are a stray lock of hair, not a limb
    private const float FALLBACK_SHARE = 1 / 3f;  // How much of the front of the body hits when no limb sticks out
    private const float OUTLINE_TOLERANCE = 1.0f; // How far (in pixels) the outline may cut a corner of the art

    /// <summary>
    /// The boxes of a frame and its outline (as pieces of line, two points each), all as shares of the frame:
    /// (0, 0) is its middle, x is the way the character faces, y is up.
    /// </summary>
    internal readonly record struct Frame(Box Hurt, Box Strike, Vector2[] Outline);

    // The sheets that have been read, they are big and every player of a character shares them
    private static readonly Dictionary<string, CharacterBoxes?> cache = [];

    private readonly Dictionary<string, Frame[]> _animations = [];

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
                Bogz.Logging.Loggers.ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, $"The hit boxes of '{directory}' couldn't be read: {exception.Message}");
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

            var frames = new Frame[Math.Max(1, animation.Length)];
            for (int i = 0; i < frames.Length; i++)
            {
                // The frames of an animation are next to each other on the sheet, counted in frames
                int left = ((int)animation.FirstFrame.Position.X + i) * width, top = (int)animation.FirstFrame.Position.Y * height;
                frames[i] = Measure(image, left, top, width, height);
            }

            _animations[name] = frames;
        }
    }

    /// <summary>
    /// Helper method to get the boxes of a frame, false for an animation or a frame the character doesn't have.
    /// </summary>
    public bool TryGet(string animation, uint frame, out Frame boxes)
    {
        if (_animations.TryGetValue(animation, out var frames) && frames.Length > 0)
        {
            boxes = frames[Math.Min(frame, (uint)frames.Length - 1)];
            return boxes.Hurt.Max.X > boxes.Hurt.Min.X;
        }

        boxes = new Frame(default, default, []);
        return false;
    }

    /// <summary>
    /// Helper method to work out the boxes of one frame of the sheet.
    /// </summary>
    private static Frame Measure(Image<Rgba32> image, int left, int top, int width, int height)
    {
        if (left < 0 || top < 0 || left + width > image.Width || top + height > image.Height) return new Frame(default, default, []);

        // What of the frame is the character, which is what the outline goes around
        var solid = new bool[width * height];

        // How much there is in every column, and the box around all of it
        Span<int> columns = width <= 512 ? stackalloc int[width] : new int[width];
        columns.Clear();

        int minX = width, maxX = -1, minY = height, maxY = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (image[left + x, top + y].A < SOLID) continue;

                solid[y * width + x] = true;
                columns[x]++;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        }

        // An empty frame can't be hit and hits nothing
        if (maxX < 0) return new Frame(default, default, []);

        // The body: around the fullest column, forwards for as long as they stay about as full
        int peak = minX;
        float fullest = 0.0f;
        for (int x = minX; x <= maxX; x++)
        {
            float amount = Smoothed(columns, x);
            if (amount > fullest) (fullest, peak) = (amount, x);
        }

        int front = peak;
        while (front + 1 <= maxX && Smoothed(columns, front + 1) >= fullest * BODY_SHARE) front++;

        // The limb: everything drawn in front of that
        int limbMinX = width, limbMaxX = -1, limbMinY = height, limbMaxY = -1, limbPixels = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = front + 1; x <= maxX; x++)
            {
                if (image[left + x, top + y].A < SOLID) continue;

                limbPixels++;
                limbMinX = Math.Min(limbMinX, x); limbMaxX = Math.Max(limbMaxX, x);
                limbMinY = Math.Min(limbMinY, y); limbMaxY = Math.Max(limbMaxY, y);
            }
        }

        if (limbPixels < LIMB_PIXELS)
        {
            // Nothing sticks out: the front of the body it is
            limbMinX = maxX + 1 - Math.Max(4, (int)((maxX + 1 - minX) * FALLBACK_SHARE));
            (limbMaxX, limbMinY, limbMaxY) = (maxX, minY, maxY);
        }

        return new Frame(
            ToShare(minX, minY, maxX, maxY, width, height),
            ToShare(limbMinX, limbMinY, limbMaxX, limbMaxY, width, height),
            Outline(solid, width, height));
    }

    /// <summary>
    /// Helper method to trace the silhouette of a frame, as the pieces of line that go around it.
    /// </summary>
    private static Vector2[] Outline(bool[] solid, int width, int height)
    {
        var pieces = new List<Vector2>();
        foreach (Vector2[] loop in MarchingSquares.Trace(solid, width, height, OUTLINE_TOLERANCE))
        {
            for (int i = 0; i < loop.Length; i++)
            {
                // Pixels count from the top left corner and downwards, we from the middle and upwards
                Vector2 from = loop[i], to = loop[(i + 1) % loop.Length];
                pieces.Add(new Vector2(from.X / width - 0.5f, 0.5f - from.Y / height));
                pieces.Add(new Vector2(to.X / width - 0.5f, 0.5f - to.Y / height));
            }
        }

        return [.. pieces];
    }

    private static float Smoothed(ReadOnlySpan<int> columns, int at)
    {
        int sum = 0, count = 0;
        for (int x = Math.Max(0, at - BODY_SMOOTHING); x <= Math.Min(columns.Length - 1, at + BODY_SMOOTHING); x++)
        {
            sum += columns[x];
            count++;
        }

        return (float)sum / count;
    }

    // Pixels count from the top left corner and downwards, the boxes from the middle and upwards
    private static Box ToShare(int minX, int minY, int maxX, int maxY, int width, int height) => new(
        new Vector2((float)minX / width - 0.5f, 0.5f - (float)(maxY + 1) / height),
        new Vector2((float)(maxX + 1) / width - 0.5f, 0.5f - (float)minY / height));
}
