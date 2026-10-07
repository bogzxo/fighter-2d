using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.Core;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fighter2D.Fighters;

/// <summary>
/// Works out the hurtbox, the hitbox and the outline of one frame of a sprite sheet by looking at its pixels.
/// The hurtbox is the box around everything that is drawn. The hitbox is whatever limb sticks out in front of the body.
/// </summary>
internal static class SpriteBoxTracer
{
    private const byte SOLID = 32;                // How much alpha a pixel needs to count as part of the character
    private const int BODY_SMOOTHING = 2;         // How many columns to either side one is averaged with, so a braid or a gap isn't mistaken for the body
    private const float BODY_SHARE = 0.5f;        // How full a column has to be against the fullest one to still be the body
    private const int LIMB_PIXELS = 12;           // Fewer pixels than this past the body are a stray lock of hair, not a limb
    private const float FALLBACK_SHARE = 1 / 3f;  // How much of the front of the body hits when no limb sticks out
    private const float OUTLINE_TOLERANCE = 1.0f; // How far (in pixels) the outline is allowed to cut a corner of the art

    private static readonly FrameBoxes Empty = new(default, default, []);

    /// <summary>
    /// Helper method to work out the boxes of the frame at the given spot of the sheet.
    /// </summary>
    public static FrameBoxes Trace(Image<Rgba32> image, int left, int top, int width, int height)
    {
        if (left < 0 || top < 0 || left + width > image.Width || top + height > image.Height) return Empty;

        // Which pixels are the character, and how many of them every column has
        var solid = new bool[width * height];
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
        if (maxX < 0) return Empty;

        int front = FindFrontOfBody(columns, minX, maxX);
        Box hitbox = FindLimb(image, left, top, front, minX, minY, maxX, maxY, width, height);

        return new FrameBoxes(ToShare(minX, minY, maxX, maxY, width, height), hitbox, TraceOutline(solid, width, height));
    }

    /// <summary>
    /// Helper method to find where the body ends, which is the last column going forwards from the fullest one that is still about as full.
    /// </summary>
    private static int FindFrontOfBody(ReadOnlySpan<int> columns, int minX, int maxX)
    {
        int peak = minX;
        float fullest = 0.0f;
        for (int x = minX; x <= maxX; x++)
        {
            float amount = Smoothed(columns, x);
            if (amount > fullest) (fullest, peak) = (amount, x);
        }

        int front = peak;
        while (front + 1 <= maxX && Smoothed(columns, front + 1) >= fullest * BODY_SHARE) front++;

        return front;
    }

    /// <summary>
    /// Helper method to box whatever is drawn in front of the body, that is the fist or the foot doing the hitting.
    /// </summary>
    private static Box FindLimb(Image<Rgba32> image, int left, int top, int front, int minX, int minY, int maxX, int maxY, int width, int height)
    {
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
            // Nothing sticks out, so the front of the body does the hitting
            limbMinX = maxX + 1 - Math.Max(4, (int)((maxX + 1 - minX) * FALLBACK_SHARE));
            (limbMaxX, limbMinY, limbMaxY) = (maxX, minY, maxY);
        }

        return ToShare(limbMinX, limbMinY, limbMaxX, limbMaxY, width, height);
    }

    /// <summary>
    /// Helper method to trace the silhouette of a frame as the line pieces that go around it.
    /// </summary>
    private static Vector2[] TraceOutline(bool[] solid, int width, int height)
    {
        var pieces = new List<Vector2>();
        foreach (Vector2[] loop in MarchingSquares.Trace(solid, width, height, OUTLINE_TOLERANCE))
        {
            for (int i = 0; i < loop.Length; i++)
            {
                Vector2 from = loop[i], to = loop[(i + 1) % loop.Length];
                pieces.Add(PixelToShare(from, width, height));
                pieces.Add(PixelToShare(to, width, height));
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

    // Pixels count from the top left corner and go down, we count from the middle and go up
    private static Vector2 PixelToShare(Vector2 pixel, int width, int height) => new(pixel.X / width - 0.5f, 0.5f - pixel.Y / height);

    private static Box ToShare(int minX, int minY, int maxX, int maxY, int width, int height) => new(
        new Vector2((float)minX / width - 0.5f, 0.5f - (float)(maxY + 1) / height),
        new Vector2((float)(maxX + 1) / width - 0.5f, 0.5f - (float)minY / height));
}
