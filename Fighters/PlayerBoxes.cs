using System;
using System.Numerics;

using Horizon.Physics;
using Horizon.Physics.Fixtures;

namespace Fighter2D.Fighters;

/// <summary>
/// The hurtbox and hitbox of a player in the world, kept in step with whatever frame they are showing.
/// Also keeps the outline fixture up to date, which is what rain runs down and blood bounces off.
/// </summary>
internal sealed class PlayerBoxes
{
    // How far a hitbox reaches past what is drawn, a hit that only just misses the eye should still land
    private const float HITBOX_REACH = 4f;

    // How much bigger than what is drawn the hurtbox is, all round
    private const float HURTBOX_PADDING = 3f;

    // The boxes of a character whose sheet couldn't be read, as a share of its sprite
    private static readonly Box FallbackBox = new(new Vector2(-0.2f, -0.5f), new Vector2(0.2f, 0.05f));

    private readonly Player _player;
    private readonly CharacterBoxes? _boxes;

    // What was last handed to the physics, it only needs telling when something changes
    private (string? Animation, uint Frame, bool Flipped) _shown;
    private Vector2[] _outline = [];

    /// <summary>
    /// Where the player can be hit, a little bigger than what is drawn of them.
    /// </summary>
    public RectanglePhysicsFixture HurtboxFixture { get; }

    /// <summary>
    /// What the player last hit with. It is only there for the frame the hit comes out on.
    /// </summary>
    public RectanglePhysicsFixture HitboxFixture { get; }

    /// <summary>
    /// What is drawn of the player to the pixel. The fight doesn't care about it, particles do.
    /// </summary>
    public OutlinePhysicsFixture OutlineFixture { get; }

    public PlayerBoxes(Player player, CharacterBoxes? boxes)
    {
        _player = player;
        _boxes = boxes;

        HurtboxFixture = player.PhysicsBody.CreateRectangularFixture(Vector2.Zero, Vector2.Zero, true, "hurtbox");
        HitboxFixture = player.PhysicsBody.CreateRectangularFixture(Vector2.Zero, Vector2.Zero, true, "hitbox");
        OutlineFixture = player.PhysicsBody.CreateOutlineFixture("outline");
    }

    /// <summary>
    /// Where the player can be hit right now, which is the box around what is drawn on the frame they are showing.
    /// </summary>
    public Box Hurtbox
    {
        get
        {
            var (animation, frame) = ShownFrame();
            return ToWorld(_boxes is not null && _boxes.TryGet(animation, frame, out var boxes) ? boxes.Hurtbox : FallbackBox, 0);
        }
    }

    /// <summary>
    /// What the player hits with on a frame of the animation they are playing, which is the limb they are throwing.
    /// </summary>
    public Box Hitbox(uint frame)
    {
        string animation = _player.Controller.ActiveAnimation;
        Box hitbox = ToWorld(_boxes is not null && _boxes.TryGet(animation, frame, out var boxes) ? boxes.Hitbox : FallbackBox, HITBOX_REACH);

        // Put it where the physics (and whoever is looking at its debug overlay) can see it
        HitboxFixture.Bounds = new PhysicsRectangle(hitbox.Min - _player.Transform.Position, hitbox.Max - hitbox.Min);
        return hitbox;
    }

    /// <summary>
    /// Called every update to move the fixtures along with the animation.
    /// </summary>
    public void Sync()
    {
        var (animation, frame) = ShownFrame();

        // Nothing to do unless what is drawn of us has changed
        if (_shown == (animation, frame, _player.Flipped)) return;
        _shown = (animation, frame, _player.Flipped);

        Box hurtbox = Hurtbox;
        HurtboxFixture.Bounds = new PhysicsRectangle(
            hurtbox.Min - _player.Transform.Position - new Vector2(HURTBOX_PADDING),
            hurtbox.Max - hurtbox.Min + new Vector2(HURTBOX_PADDING * 2));

        // A hitbox is gone with the frame it came out on
        HitboxFixture.Bounds = new PhysicsRectangle(Vector2.Zero, Vector2.Zero);

        // What stands on the map is shaped like the frame as well, somebody lying down is long and low
        _player.Body.Fit(_boxes is not null && _boxes.TryGet(animation, frame, out var traced) ? traced : null);

        SyncOutline(animation, frame);
    }

    /// <summary>
    /// Helper method to hand the silhouette of the frame to the physics, at the size and the way round we are drawn.
    /// </summary>
    private void SyncOutline(string animation, uint frame)
    {
        Vector2[] pieces = _boxes is not null && _boxes.TryGet(animation, frame, out var boxes) ? boxes.Outline : [];
        if (_outline.Length < pieces.Length) _outline = new Vector2[pieces.Length];

        Vector2 size = Vector2.Abs(_player.Transform.Size) * new Vector2(_player.Flipped ? -1 : 1, 1);
        for (int i = 0; i < pieces.Length; i++) _outline[i] = pieces[i] * size;

        OutlineFixture.Set(_outline.AsSpan(0, pieces.Length));
    }

    private (string Animation, uint Frame) ShownFrame()
    {
        string animation = _player.Controller.ActiveAnimation;
        return (animation, (uint)_player.Frame);
    }

    /// <summary>
    /// Helper method to put a box of the sprite (a share of it, facing right) where it is in the world, the way the player is facing.
    /// </summary>
    private Box ToWorld(Box share, float grow)
    {
        Vector2 size = Vector2.Abs(_player.Transform.Size);
        float minX = share.Min.X, maxX = share.Max.X;
        if (_player.Flipped) (minX, maxX) = (-maxX, -minX);

        return new Box(
            _player.Transform.Position + new Vector2(minX, share.Min.Y) * size - new Vector2(grow),
            _player.Transform.Position + new Vector2(maxX, share.Max.Y) * size + new Vector2(grow));
    }
}
