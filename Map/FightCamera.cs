using System;
using System.Numerics;

using Horizon.Engine;

namespace Fighter2D.Map;

/// <summary>
/// The camera of a fight. It frames both fighters, follows them smoothly, stays inside the map and gets rattled about by hits and thunder.
/// </summary>
internal sealed class FightCamera
{
    /// <summary>
    /// How much of the usual view the camera of a fight sees. Three quarters of it, which is everything a third bigger than the art is drawn anywhere else.
    /// Lower is closer in, 1 is no zoom at all.
    /// </summary>
    public const float ZOOM = 0.75f;

    // One pixel of the screen in world units (the art is drawn at twice its size, and then zoomed in on)
    private const float PIXEL = 0.5f * ZOOM;

    // How little the camera has to move in a step to count as standing still
    private const float STILL = 0.02f;

    // How quickly it catches up with the fight, and how far (squared) the fight has to get away before it bothers
    private const float FOLLOW_SPEED = 2.0f;
    private const float FOLLOW_DISTANCE_SQUARED = 1000.0f;

    // How close to the edge of the view (in world units) our own player is allowed to get before the camera stops looking at the other one
    private const float EDGE_MARGIN = 72.0f;

    private readonly Camera2D _camera;
    private readonly FightingStage _stage;
    private readonly Vector2 _viewportSize;

    // Where the camera would be if it didn't have to sit on whole pixels
    private Vector3 _exactPosition;

    // How far the shake has it off from where it belongs right now
    private Vector2 _shake;

    public FightCamera(Camera2D camera, FightingStage stage, Vector2 viewportSize, Vector2 start)
    {
        _camera = camera;
        _camera.Zoom = ZOOM;
        _stage = stage;
        _viewportSize = viewportSize;

        LookAt(start);
    }

    // Half of what the camera sees, in world units
    private Vector2 HalfView => _viewportSize / 2.0f * _camera.Zoom / 2.0f;

    /// <summary>
    /// Puts the camera on a spot right now, without any of the smoothing. For the start of a fight and for anybody who moves it themselves.
    /// </summary>
    public void LookAt(Vector2 focus)
    {
        _exactPosition = ClampToMap(new Vector3(focus, 0.0f));
        _camera.Position = SnapToPixels(_exactPosition, Vector2.Zero);
        _shake = Vector2.Zero;
    }

    /// <summary>
    /// Called every update with how far the screen shake wants the view off. Only the change since last time gets applied.
    /// </summary>
    public void Shake(Vector2 shake)
    {
        if (shake == _shake) return;

        _camera.Position += new Vector3(shake - _shake, 0.0f);
        _shake = shake;
    }

    /// <summary>
    /// Called once per physics step to move the camera along with the fight.
    /// It has to happen right after the step and not in the state update, because the physics has its own loop.
    /// A camera that follows from the state update sees the players stand still one moment and go twice as far the next, which makes them flicker like crazy.
    /// </summary>
    /// <param name="us">The player this machine cares about most, they never leave the screen.</param>
    /// <param name="them">The other player, who is kept on screen for as long as that doesn't push ours off it.</param>
    public void Follow(Vector2 us, Vector2 them, float dt)
    {
        Vector2 focus = Frame(us, them);

        if (Vector3.DistanceSquared(_camera.Position, new Vector3(focus, _camera.Position.Z)) <= FOLLOW_DISTANCE_SQUARED) return;

        // Follow the fight, but stop at the edges of the map so the clear colour is never seen
        Vector3 target = ClampToMap(new Vector3(focus, 0.0f));
        float smoothFactor = 1.0f - MathF.Exp(-FOLLOW_SPEED * dt);

        Vector3 before = _exactPosition;
        _exactPosition = Vector3.Lerp(_exactPosition, target, smoothFactor);

        // On the move the camera stays a whole number of screen pixels away from what it follows instead of on whole pixels of its own.
        // Rounded by itself it steps at different moments than the players do, and they wobble by a pixel.
        // As good as still (up against the edge of the map) it just sits on the pixels of the screen
        bool moving = Vector3.DistanceSquared(before, _exactPosition) > STILL * STILL;
        _camera.Position = SnapToPixels(_exactPosition, moving ? focus : Vector2.Zero);

        // The position was just set from scratch, so whatever shake was on it is gone
        _shake = Vector2.Zero;
    }

    /// <summary>
    /// Helper method to pick the spot to look at, which is halfway between the two players unless that would lose ours off the edge.
    /// </summary>
    private Vector2 Frame(Vector2 us, Vector2 them)
    {
        Vector2 reach = Vector2.Max(Vector2.Zero, HalfView - new Vector2(EDGE_MARGIN));
        Vector2 middle = (us + them) / 2.0f;

        return Vector2.Clamp(middle, us - reach, us + reach);
    }

    /// <summary>
    /// Helper method to round a position to the pixels of the screen, counted from an anchor.
    /// </summary>
    private static Vector3 SnapToPixels(Vector3 position, Vector2 anchor) => new(
        anchor.X + MathF.Round((position.X - anchor.X) / PIXEL) * PIXEL,
        anchor.Y + MathF.Round((position.Y - anchor.Y) / PIXEL) * PIXEL,
        position.Z);

    /// <summary>
    /// Helper method to keep what the camera sees inside of the map.
    /// </summary>
    private Vector3 ClampToMap(Vector3 position)
    {
        if (!_stage.HasBounds) return position;

        Vector2 halfView = HalfView;

        return new Vector3(
            ClampAxis(position.X, _stage.Min.X, _stage.Max.X, halfView.X),
            ClampAxis(position.Y, _stage.Min.Y, _stage.Max.Y, halfView.Y),
            position.Z);
    }

    private static float ClampAxis(float position, float min, float max, float halfView)
    {
        // A map smaller than the view can't fill it, the best we can do is centre it
        if (max - min <= halfView * 2) return (min + max) / 2;

        return Math.Clamp(position, min + halfView, max - halfView);
    }
}
