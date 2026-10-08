using System;
using System.Numerics;

using Horizon.Physics;
using Horizon.Physics.Fixtures;

namespace Fighter2D.Fighters;

/// <summary>
/// The part of a player that stands on the map and bumps into it: a box around the bulk of whatever frame they are showing,
/// and under it the feet, which is what says whether they are on the ground. Both are worked out from the art the same way the
/// hitboxes are (see <see cref="SpriteBoxTracer"/>), so somebody who has fallen over is as long and as low as they are drawn.
/// They lie across a gap they would have dropped into standing up, and don't end up with half of them inside a wall.
/// <para>
/// The physics only ever stops a body from moving into something, it never pushes one back out. So a box that grows asks
/// first whether there is room, and if there isn't the player is shifted over until there is: falling over next to a wall
/// slides them away from it. If there is no room anywhere near the box stays the size it was.
/// </para>
/// </summary>
internal sealed class PlayerBody
{
    // The smallest the box gets, in pixels of the art. A frame with next to nothing drawn on it still has to stand on something
    private const float MIN_WIDTH = 20.0f;
    private const float MIN_HEIGHT = 20.0f;

    // The box of a character whose art couldn't be read, as a share of its frame. About somebody standing there
    private static readonly Box Standing = new(new Vector2(-0.12f, -0.5f), new Vector2(0.12f, 0.06f));

    // How far the feet reach below the box and how much narrower than it they are on either side, in pixels of the art.
    // Narrower so that brushing a wall isn't standing on it
    private const float FEET_DEPTH = 8.0f;
    private const float FEET_INSET = 2.0f;

    // How far (in pixels of the art) a player is shifted over at the most to make room for their box, and in what steps
    private const float MAX_SHIFT = 72.0f;
    private const float SHIFT_STEP = 2.0f;

    // What counts as lying down: drawn no taller than this share of the frame, and at least this many times as long as that
    private const float LYING_HEIGHT = 0.3f;
    private const float LYING_ASPECT = 1.5f;

    private readonly Player _player;
    private readonly PhysicsWorld _world;

    private readonly RectanglePhysicsFixture _box;
    private readonly RectanglePhysicsFixture _feet;

    public PlayerBody(Player player, PhysicsWorld world)
    {
        _player = player;
        _world = world;

        _box = player.PhysicsBody.CreateRectangularFixture(Vector2.Zero, Vector2.One);
        _feet = player.PhysicsBody.CreateRectangularFixture(Vector2.Zero, Vector2.One, true, Player.FEET_TAG);

        Fit(null);
    }

    /// <summary>
    /// Makes the box and the feet fit a frame of animation.
    /// </summary>
    /// <param name="frame">The boxes of the frame, null for a frame that has none.</param>
    public void Fit(FrameBoxes? frame)
    {
        Vector2 size = Vector2.Abs(_player.Transform.Size);
        float scale = _player.Scale;

        Box share = frame is { Body: var body } && body.Max.X > body.Min.X ? body : Standing;

        // Somebody who is longer than they are tall is lying down, and then all of them is on the floor: head, feet and everything
        // between. Standing up the bulk of them is the trunk, a leg stuck out in a kick doesn't hold anybody up
        if (frame is { Hurtbox: var drawn } && IsLyingDown(drawn))
            share = new Box(new Vector2(drawn.Min.X, share.Min.Y), new Vector2(drawn.Max.X, share.Max.Y));

        // The way round the player is facing
        float left = share.Min.X, right = share.Max.X;
        if (_player.Flipped) (left, right) = (-right, -left);

        // As wide as the bulk of what is drawn, never narrower than somebody can stand on
        float width = MathF.Max((right - left) * size.X, MIN_WIDTH * scale);
        float middle = (left + right) * 0.5f * size.X;

        // From the bottom of the frame, which is where the floor is whatever the art is doing, up to the top of what is drawn
        float bottom = -0.5f * size.Y;
        float height = MathF.Max((share.Max.Y + 0.5f) * size.Y, MIN_HEIGHT * scale);

        var wanted = new PhysicsRectangle(new Vector2(middle - width * 0.5f, bottom), new Vector2(width, height));
        if (!MakeRoom(wanted)) return;

        _box.Bounds = wanted;
        _feet.Bounds = new PhysicsRectangle(
            new Vector2(wanted.Left + FEET_INSET * scale, bottom - FEET_DEPTH * scale),
            new Vector2(MathF.Max(1.0f, width - FEET_INSET * scale * 2.0f), FEET_DEPTH * scale));
    }

    /// <summary>
    /// Helper method to test if what is drawn on a frame is somebody on the floor: a good deal longer than tall, and nowhere
    /// near as tall as the frame. A high kick is wide as well, but that is somebody standing on one leg.
    /// </summary>
    private static bool IsLyingDown(Box drawn)
    {
        float width = drawn.Max.X - drawn.Min.X, height = drawn.Max.Y - drawn.Min.Y;
        return height < LYING_HEIGHT && width > height * LYING_ASPECT;
    }

    /// <summary>
    /// Helper method to see to it that a box fits where the player is, by moving them over if it doesn't.
    /// </summary>
    /// <returns>False if there is no room for it anywhere near, the player is left where they are then.</returns>
    private bool MakeRoom(PhysicsRectangle wanted)
    {
        PhysicsBodyComponent2D body = _player.PhysicsBody;
        PhysicsRectangle before = _box.Bounds;

        // Tried on for size. A hair off the floor, standing on something isn't being inside of it
        const float clearance = 0.5f;
        _box.Bounds = new PhysicsRectangle(wanted.Position + new Vector2(0.0f, clearance), wanted.Size - new Vector2(0.0f, clearance));

        try
        {
            if (!_world.OverlapsStatic(_box, body.Position)) return true;

            float reach = MAX_SHIFT * _player.Scale;
            for (float shift = SHIFT_STEP; shift <= reach; shift += SHIFT_STEP)
            {
                foreach (float sideways in (ReadOnlySpan<float>)[shift, -shift])
                {
                    Vector2 moved = body.Position + new Vector2(sideways, 0.0f);
                    if (_world.OverlapsStatic(_box, moved)) continue;

                    body.Position = moved;
                    _player.Transform.Position = moved;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            _box.Bounds = before;
        }
    }
}
