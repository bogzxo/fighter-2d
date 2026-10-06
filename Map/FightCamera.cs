using System;
using System.Numerics;

using Horizon.Engine;

namespace Fighter2D.Map;

/// <summary>
/// The camera of a fight. It follows a player smoothly, stays inside the map and gets rattled about by hits and thunder.
/// </summary>
internal sealed class FightCamera
{
    // One pixel of the screen in world units (the art is drawn at twice its size)
    private const float PIXEL = 0.5f;

    // How little the camera has to move in a step to count as standing still
    private const float STILL = 0.02f;

    // How quickly it catches up with the player, and how far (squared) the player has to get away before it bothers
    private const float FOLLOW_SPEED = 2.0f;
    private const float FOLLOW_DISTANCE_SQUARED = 1000.0f;

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
        _stage = stage;
        _viewportSize = viewportSize;

        _camera.Position = ClampToMap(new Vector3(start, 0.0f));
        _exactPosition = _camera.Position;
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
    /// Called once per physics step to move the camera along with a player.
    /// It has to happen right after the step and not in the state update, because the physics has its own loop.
    /// A camera that follows from the state update sees the player stand still one moment and go twice as far the next, which makes them flicker like crazy.
    /// </summary>
    public void Follow(Vector2 player, float dt)
    {
        if (Vector3.DistanceSquared(_camera.Position, new Vector3(player, _camera.Position.Z)) <= FOLLOW_DISTANCE_SQUARED) return;

        // Follow the player, but stop at the edges of the map so the clear colour is never seen
        Vector3 target = ClampToMap(new Vector3(player, 0.0f));
        float smoothFactor = 1.0f - MathF.Exp(-FOLLOW_SPEED * dt);

        Vector3 before = _exactPosition;
        _exactPosition = Vector3.Lerp(_exactPosition, target, smoothFactor);

        if (Vector3.DistanceSquared(before, _exactPosition) > STILL * STILL)
        {
            // On the move. Keep the camera a whole number of screen pixels away from the player instead of on whole pixels of its own.
            // Rounded by itself it steps at different moments than the player does and the player wobbles by a pixel
            _camera.Position = new Vector3(
                player.X + MathF.Round((_exactPosition.X - player.X) / PIXEL) * PIXEL,
                player.Y + MathF.Round((_exactPosition.Y - player.Y) / PIXEL) * PIXEL,
                _exactPosition.Z);
        }
        else
        {
            // As good as still (up against the edge of the map), so it sits on the pixels of the screen
            _camera.Position = new Vector3(
                MathF.Round(_exactPosition.X / PIXEL) * PIXEL,
                MathF.Round(_exactPosition.Y / PIXEL) * PIXEL,
                _exactPosition.Z);
        }

        // The position was just set from scratch, so whatever shake was on it is gone
        _shake = Vector2.Zero;
    }

    /// <summary>
    /// Helper method to keep what the camera sees inside of the map.
    /// </summary>
    private Vector3 ClampToMap(Vector3 position)
    {
        if (!_stage.HasBounds) return position;

        Vector2 halfView = _viewportSize / 2.0f * _camera.Zoom / 2.0f;

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
