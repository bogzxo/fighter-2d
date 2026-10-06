using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.Rendering;
using Horizon.Rendering.Lighting;
using Horizon.Rendering.Particles;

namespace Fighter2D.Effects;

/// <summary>
/// The spots of a map that keep leaking particles (a dripping pipe, a crack with lava running out of it).
/// A map places them in Tiled by giving an object an emitter_type and an emitter_rate.
/// </summary>
internal sealed class MapEmitters
{
    // How far under an emitter its light sits, since what comes out falls and most of the glow is below it
    private const float LIGHT_DROP = 24f;

    /// <summary>
    /// How one kind of emitter lets its particles go. They all fall out of it downwards.
    /// </summary>
    /// <param name="Light">A light every emitter of this kind gets a copy of, for the stuff that glows.</param>
    /// <param name="Density">How many particles one unit of the map's emitter_rate stands for.</param>
    /// <param name="Width">How far to either side of the emitter they come out, for a stream that is more than one drop wide.</param>
    private readonly record struct Preset(ParticleRenderer2D Renderer, float Spread, float MinSpeed, float MaxSpeed, Light2D? Light = null, float Density = 1.0f, float Width = 0.0f);

    private sealed class Emitter(Preset preset, Vector2 position, float rate)
    {
        public readonly Preset Preset = preset;
        public readonly Vector2 Position = position;

        // Particles per second
        public readonly float Rate = rate * preset.Density;
        public float Timer;
    }

    private readonly Dictionary<string, Preset> _presets;
    private readonly List<Emitter> _emitters = [];
    private readonly DeferredRenderer2D _lighting;

    public MapEmitters(FightParticles particles, DeferredRenderer2D lighting)
    {
        _lighting = lighting;

        // The names are the ones a map gives its emitters ("emitter_type")
        _presets = new(StringComparer.OrdinalIgnoreCase)
        {
            // Two drops wide and no more of them than fit out side by side, let go on top of one another they only burst apart
            ["water"] = new Preset(particles.Water, 0.2f, 40, 60, Density: 2.0f, Width: 2.5f),
            ["lava"] = new Preset(particles.Lava, 0.25f, 4, 18, new Light2D
            {
                Color = new Vector3(1.0f, 0.45f, 0.12f),
                Radius = 150.0f,
                Intensity = 0.6f,
                Glow = 0.22f,
                Flicker = 0.25f,
                Size = 8.0f
            })
        };
    }

    /// <summary>
    /// Places an emitter, from then on it leaks particles by itself.
    /// </summary>
    /// <param name="kind">The name of the preset to use ("water", "lava").</param>
    /// <param name="rate">How many particles it lets go every second.</param>
    /// <returns>False if there is no such preset, or nothing would ever come out of it.</returns>
    public bool Add(string kind, Vector2 position, float rate)
    {
        if (rate <= 0.0f || !_presets.TryGetValue(kind, out var preset)) return false;

        _emitters.Add(new Emitter(preset, position, rate));

        if (preset.Light is Light2D light)
        {
            _lighting.AddLight(new Light2D
            {
                Position = position - Vector2.UnitY * LIGHT_DROP,
                Color = light.Color,
                Radius = light.Radius,
                Intensity = light.Intensity,
                Glow = light.Glow,
                Flicker = light.Flicker,
                Size = light.Size
            });
        }

        return true;
    }

    public void Update(float dt)
    {
        foreach (Emitter emitter in _emitters)
        {
            // However many are due by now, so the rate holds no matter how long the frame was
            emitter.Timer += dt;
            int due = (int)(emitter.Timer * emitter.Rate);
            if (due < 1) continue;

            emitter.Timer -= due / emitter.Rate;

            Preset preset = emitter.Preset;
            ParticleSpray.Cone(preset.Renderer, emitter.Position, -Vector2.UnitY, preset.Spread, due, preset.MinSpeed, preset.MaxSpeed, preset.Width);
        }
    }
}
