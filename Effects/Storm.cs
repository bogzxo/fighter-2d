using System;
using System.Numerics;

using Fighter2D.Map;

using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Rendering;
using Horizon.Rendering.Lighting;

namespace Fighter2D.Effects;

/// <summary>
/// The thunderstorm over a map. Every so often a bolt lights everything up, flickers once more and rattles the screen a moment later.
/// The whole thing is tweens, the wait for the next bolt, the two flashes of a bolt and the fade of each flash.
/// </summary>
internal sealed class Storm
{
    private const float FLASH_FADE_TIME = 0.75f;  // How long (in seconds) a flash of lightning takes to die away
    private const float FLASH_LIGHT_TIME = 0.3f;  // How long (in seconds) the light a bolt throws lasts
    private const float FLICKER_STRENGTH = 0.6f;  // How bright the second flash of a bolt is against the first
    private const float RUMBLE_TIME = 1.1f;       // How long (in seconds) the thunder rattles the screen for

    // Only one flash fades at a time, the second flash of a bolt takes over from the first
    private const string FLASH_CHANNEL = "flash";

    private readonly StormDefinition _storm;
    private readonly Camera2D _camera;
    private readonly DeferredRenderer2D _lighting;
    private readonly TweenContext _tweens = new();

    // The light the map has when nothing is flashing
    private readonly Vector3 _ambient;

    // Thunder rumbles slower and for longer than a hit does
    private readonly ScreenShake _rumble = new() { Speed = 34.0f, Easing = Easing.OutQuad, VerticalSpeed = 1.37f, VerticalPhase = 1.3f, VerticalAmount = 1.0f };

    // How bright the sky still is from the last flash, 0 when it is dark
    private float _flash;

    public Vector2 Shake => _rumble.Offset;

    public Storm(StormDefinition storm, Camera2D camera, DeferredRenderer2D lighting)
    {
        _storm = storm;
        _camera = camera;
        _lighting = lighting;
        _ambient = lighting.Ambient;

        ScheduleBolt();
    }

    public void Update(float dt)
    {
        _tweens.Tick(dt);
        _rumble.Update(dt);
    }

    private void ScheduleBolt()
    {
        float wait = _storm.MinInterval + Random.Shared.NextSingle() * MathF.Max(0.0f, _storm.MaxInterval - _storm.MinInterval);

        _tweens.Play(Tween.Wait(wait).OnComplete(Strike));
    }

    /// <summary>
    /// Helper method for one bolt of lightning. It flashes twice, and is heard a moment after it is seen.
    /// </summary>
    private void Strike()
    {
        ScheduleBolt();

        float untilFlicker = 0.08f + Random.Shared.NextSingle() * 0.1f;
        float untilThunder = 0.35f + Random.Shared.NextSingle() * 0.8f;

        _tweens.Play(
            Tween.Sequence()
                .AppendCallback(() => Flash(1.0f))
                .AppendTime(untilFlicker)
                .AppendCallback(() => Flash(FLICKER_STRENGTH))
                .AppendTime(untilThunder - untilFlicker)
                .AppendCallback(() => _rumble.Kick(_storm.Shake, RUMBLE_TIME))
                .Build());
    }

    /// <summary>
    /// Helper method to flash the sky. Everything is lit up for a moment, and a light from where the bolt came down throws the shadows that go with it.
    /// </summary>
    private void Flash(float strength)
    {
        _flash = MathF.Max(_flash, strength);
        _tweens.Play(Tween.To(() => _flash, SetFlash, 0.0f, FLASH_FADE_TIME).SetEasing(Easing.OutExpo), FLASH_CHANNEL);

        var view = _camera.Bounds;
        _lighting.AddFlash(new Light2D
        {
            Position = new Vector2(view.X + Random.Shared.NextSingle() * view.Width, view.Y + view.Height + 40.0f),
            Color = _storm.Color,
            Radius = view.Width * 1.2f,
            Intensity = _storm.Brightness * 2.0f * strength,
            Height = 160.0f
        }, FLASH_LIGHT_TIME);
    }

    private void SetFlash(float flash)
    {
        _flash = flash;

        // The sky lights up everything at once, which is what the ambient light of the map is for
        _lighting.Ambient = _ambient + _storm.Color * (_storm.Brightness * flash);
    }
}
