using System;
using System.Numerics;

using Horizon.Core.Tweening;

namespace Fighter2D.Effects;

/// <summary>
/// A screen shake that rattles hard and dies away quickly, hits and thunder both use one of these.
/// It is a tween like everything else that animates, it just has nothing to move by itself. Whoever owns the camera adds <see cref="Offset"/> to it.
/// The offset comes out in whole pixels of the art so the pixel art stays crisp.
/// </summary>
internal sealed class ScreenShake
{
    // Anything weaker than this isn't worth moving the camera for
    private const float MIN_REACH = 0.5f;

    // Only one shake plays at a time, a new one takes over from the old
    private const string CHANNEL = "shake";

    // How fast it rattles, and the curve it dies away on
    public float Speed { get; init; } = 55.0f;
    public Easing Easing { get; init; } = Easing.OutCubic;

    // The up and down part rattles at its own speed, phase and strength so the shake doesn't just go diagonally
    public float VerticalSpeed { get; init; } = 1.3f;
    public float VerticalPhase { get; init; } = 0.9f;
    public float VerticalAmount { get; init; } = 0.6f;

    private readonly TweenContext _tweens = new();

    // How hard the shake that is playing started out, how much of it is left (1 to 0) and how far along its rattle it is
    private float _strength, _left, _phase;

    /// <summary>
    /// How far the view is off from where it belongs right now. Zero most of the time.
    /// </summary>
    public Vector2 Offset { get; private set; }

    /// <summary>
    /// Starts a shake, unless one that is still shaking harder than this is already playing.
    /// </summary>
    /// <param name="strength">How far it throws the view at most, in pixels of the art.</param>
    /// <param name="duration">How long it takes to die away, in seconds.</param>
    public void Kick(float strength, float duration)
    {
        if (_strength * _left >= strength) return;

        _strength = strength;
        _left = 1.0f;
        _phase = 0.0f;

        // Two tweens side by side, one fades the shake out and the other runs the rattle along at a steady speed
        _tweens.Play(
            Tween.Sequence()
                .Append(Tween.To(() => 1.0f, left => _left = left, 0.0f, duration).SetEasing(Easing))
                .Join(Tween.To(() => 0.0f, phase => _phase = phase, Speed * duration, duration))
                .Build(),
            CHANNEL);
    }

    public void Update(float dt)
    {
        _tweens.Tick(dt);

        float reach = _strength * _left;
        Offset = reach < MIN_REACH ? Vector2.Zero : new Vector2(
            MathF.Round(MathF.Sin(_phase) * reach),
            MathF.Round(MathF.Sin(_phase * VerticalSpeed + VerticalPhase) * reach * VerticalAmount));
    }
}
