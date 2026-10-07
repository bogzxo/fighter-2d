using System.Numerics;

using Horizon.Core.Tweening;

namespace Fighter2D.Scenes;

/// <summary>
/// A value that swings back and forth forever, for text that should pulse to get somebody's attention.
/// It is a looping tween, whoever owns it has to tick it once per update.
/// </summary>
internal sealed class Pulse
{
    private readonly TweenContext _tweens = new();

    public float Value { get; private set; }

    /// <summary>
    /// The value as a shade of grey, which is what a pulsing label gets coloured with.
    /// </summary>
    public Vector4 Grey => new(Value, Value, Value, 1.0f);

    /// <param name="period">How long (in seconds) one swing there and back takes.</param>
    public Pulse(float from, float to, float period)
    {
        Value = from;
        _tweens.Play(Tween.To(() => Value, value => Value = value, to, period / 2).SetEasing(Easing.InOutSine).SetLoops(-1, LoopMode.PingPong));
    }

    public void Update(float dt) => _tweens.Tick(dt);
}
