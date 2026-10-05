using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.Engine;
using Horizon.Physics;
using Horizon.Physics.Simulation;
using Horizon.Rendering;
using Horizon.Rendering.Lighting;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Particles.Simulation;

namespace Fighter2D.Effects;

/// <summary>
/// Every particle effect of a fight: the sparks and dust thrown up by the players, and the ambience drifting through the map.
/// The controllers and move routines only say what happened (a hit, a landing etc.), how it looks is decided here.
/// </summary>
internal class FightEffects : GameObject
{
    private const int MaxSprayCount = 128;
    private const float AMBIENCE_MARGIN = 96f;    // How far outside of the view the ambience spawns, so it never pops in
    private const float EMITTER_LIGHT_DROP = 24f; // How far underneath an emitter the light of what it lets go is

    private static readonly Vector3 FLASH_COLOUR = new(1.0f, 0.85f, 0.55f);

    private readonly ParticleRenderer2D _sparks, _dust, _haze, _ambience, _water, _lava;
    private readonly Camera2D _camera;
    private readonly PhysicsWorld _world;
    private readonly DeferredRenderer2D _lighting;
    private readonly bool _rising;
    private float _ambienceTimer = 0.0f;

    // How an emitter of a kind lets its particles go, they all fall out of it downwards.
    // The ones that let something glowing go come with a light as well, which every emitter gets a copy of.
    private readonly record struct EmitterPreset(ParticleRenderer2D Renderer, float Spread, float MinSpeed, float MaxSpeed, Light2D? Light = null);

    // A spot of the map that keeps letting particles go (a leaking pipe, a crack with lava running out of it)
    private sealed class Emitter(EmitterPreset preset, Vector2 position, float rate)
    {
        public readonly EmitterPreset Preset = preset;
        public readonly Vector2 Position = position;
        public readonly float Rate = rate;      // Particles per second
        public float Timer;
    }

    private readonly Dictionary<string, EmitterPreset> _emitterPresets;
    private readonly List<Emitter> _emitters = [];


    /// <param name="camera">The camera the fight is seen through, the ambience is kept around it.</param>
    /// <param name="ambience">The parsed ambience definition properties.</param>
    /// <param name="lighting">The renderer the fight is lit by, which the flashes and the glow of what is hot are added to.</param>
    public FightEffects(Camera2D camera, MapLoader.AmbienceDefinition ambience, PhysicsWorld world, DeferredRenderer2D lighting)
    {
        Name = "Fight Effects";

        _camera = camera;
        _world = world;
        _lighting = lighting;

        // We determine whether the particles spawn from the bottom or top based on gravity direction
        _rising = ambience.Gravity.Y > 0;

        // Petals sink on the wind, embers rise (customized heavily by maps now)
        // These collide with the physics world (on the GPU), so they land on the map and the players can kick them about
        var ambienceSimulator = new PhysicsParticleSimulator2D(world);
        ambienceSimulator.Radius = 1.5f;
        ambienceSimulator.Restitution = 0.25f;
        ambienceSimulator.Friction = 5.0f;

        // They drift along slowly, so they are only wafted aside (by a player or a shockwave) at about the speed they drift at
        ambienceSimulator.Mass = 3.0f;
        ambienceSimulator.BodyPushLimit = 70.0f;
        
        var sparksSimulator = new PhysicsParticleSimulator2D(world);
        sparksSimulator.Radius = 1.5f;
        sparksSimulator.Restitution = 0.25f;
        sparksSimulator.Friction = 5.0f;

        // Weightless as far as pushes go: the shockwave of the hit they come from would only blow them all away
        sparksSimulator.Mass = 0.0f;

        // The liquids collide with themselves too, so they pool where they land and overflow once that is full.
        // Runny, it levels out quickly
        var waterSimulator = new PhysicsFluidParticleSimulator2D(world);
        waterSimulator.Particles.Radius = 1.0f;
        waterSimulator.Particles.Friction = 1.0f;
        waterSimulator.MaxLife = 15;

        // Enough for a splash when someone drops into it, wading through only parts it
        waterSimulator.Particles.BodyPush = 0.15f;
        waterSimulator.Particles.BodyPushLimit = 110.0f;

        // Thick, it heaps up and only creeps outwards once it is down
        var lavaSimulator = new PhysicsFluidParticleSimulator2D(world);
        lavaSimulator.Particles.Radius = 1.5f;
        lavaSimulator.Particles.Friction = 14.0f;
        lavaSimulator.Particles.LinearDrag = 1.5f;
        lavaSimulator.Particles.Mass = 3.0f;
        lavaSimulator.Particles.BodyPush = 0.05f;
        lavaSimulator.Particles.BodyPushLimit = 60.0f;
        lavaSimulator.MaxLife = 15;

        // Slow and long lived
        _water = AddEntity(new ParticleRenderer2D(8192 * 2, waterSimulator)
        {
            StartColor = new Vector3(3 / 255.0f, 98 / 255.0f, 252 / 255.0f),
            EndColor = new Vector3(3 / 255.0f, 169 / 255.0f, 252 / 255.0f),
            ParticleSize = 1.5f,
            MaxAge = 20.0f,
            Gravity = new Vector2(0, -300)
        });

        // Glowing as it comes out, it cools to a dark crust where it ends up lying
        _lava = AddEntity(new ParticleRenderer2D(8192, lavaSimulator)
        {
            StartColor = new Vector3(1.0f, 0.62f, 0.1f),
            EndColor = new Vector3(0.35f, 0.04f, 0.0f),
            ParticleSize = 2.5f,
            MaxAge = 14.0f,
            Gravity = new Vector2(0, -180),

            // It is its own light, until it has cooled into a crust
            StartEmissive = 1.0f,
            EndEmissive = 0.15f
        });

        // The names are the ones a map gives its emitters ("emitter_type")
        _emitterPresets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["water"] = new EmitterPreset(_water, 0.5f, 10, 45),
            ["lava"] = new EmitterPreset(_lava, 0.25f, 4, 18, new Light2D
            {
                Color = new Vector3(1.0f, 0.45f, 0.12f),
                Radius = 150.0f,
                Intensity = 1.6f,
                Glow = 0.22f,
                Flicker = 0.25f,
                Size = 8.0f
            })
        };

        // Hot and short lived, they arc away from a hit
        _sparks = AddEntity(new ParticleRenderer2D(8192, sparksSimulator)
        {
            StartColor = new Vector3(1.0f, 0.95f, 0.6f),
            EndColor = new Vector3(0.5f, 0.1f, 0.0f),
            ParticleSize = 1.5f,
            MaxAge = 0.45f,
            Gravity = new Vector2(0, -900),
            Emissive = 1.0f
        });

        // Pale and slow, it hangs around the feet
        _dust = AddEntity(new ParticleRenderer2D(8192, new ComputeParticleSimulator2D())
        {
            StartColor = new Vector3(0.85f, 0.82f, 0.75f),
            EndColor = new Vector3(0.3f, 0.3f, 0.3f),
            ParticleSize = 2.0f,
            MaxAge = 0.6f,
            Gravity = new Vector2(0, 60)
        });

        // Thick and slow to lift, it clouds the head of whoever is stunned
        _haze = AddEntity(new ParticleRenderer2D(4096, new ComputeParticleSimulator2D())
        {
            StartColor = new Vector3(0.8f, 0.7f, 1.0f),
            EndColor = new Vector3(0.3f, 0.2f, 0.5f),
            ParticleSize = 2.5f,
            MaxAge = 0.9f,
            Gravity = new Vector2(0, 30),

            // Whoever is stunned has to be seen to be, wherever they are standing
            Emissive = 0.7f
        });


        _ambience = AddEntity(new ParticleRenderer2D(4096, ambienceSimulator)
        {
            StartColor = ambience.StartColor,
            EndColor = ambience.EndColor,
            ParticleSize = 1.5f,
            MaxAge = 9.0f,
            Gravity = ambience.Gravity
        });
    }

    /// <summary>
    /// Places an emitter of the map, from then on it lets particles go by itself.
    /// </summary>
    /// <param name="kind">The name of the preset to use ("water", "lava").</param>
    /// <param name="rate">How many particles it lets go every second.</param>
    /// <returns>False if there is no such preset, or nothing would ever come out of it.</returns>
    public bool AddEmitter(string kind, Vector2 position, float rate)
    {
        if (rate <= 0.0f || !_emitterPresets.TryGetValue(kind, out var preset)) return false;

        _emitters.Add(new Emitter(preset, position, rate));

        if (preset.Light is Light2D light)
        {
            // What comes out falls, so most of the glow is underneath where it comes out of
            _lighting.AddLight(new Light2D
            {
                Position = position - Vector2.UnitY * EMITTER_LIGHT_DROP,
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

    /// <summary>
    /// Launches whatever is lying around a spot (the rain on the floor etc.) away from it, for the harder moves.
    /// The players themselves are left alone, pushing them about is up to the moves.
    /// </summary>
    /// <param name="position">Where on the ground the move happened.</param>
    public void Shockwave(Vector2 position, float radius, float strength)
    {
        // From slightly under the ground, so that what lies on it is thrown up rather than pressed along it
        _world.ApplyRadialImpulse(position - Vector2.UnitY * 12, radius, strength, PhysicsRadialTargets.Particles);
    }

    /// <summary>
    /// A kick has landed, heavy hits (the ones that launch) throw a lot more sparks.
    /// </summary>
    /// <param name="direction">The way the kick was going, -1 for left and 1 for right.</param>
    public void Hit(Vector2 position, float direction, bool heavy)
    {
        Spray(_sparks, position, new Vector2(direction, 0.5f), 1.4f, heavy ? 110 : 40, 60, heavy ? 480 : 300);

        // The sparks light up whatever is around for as long as they last
        _lighting.AddFlash(new Light2D
        {
            Position = position,
            Color = FLASH_COLOUR,
            Radius = heavy ? 230.0f : 140.0f,
            Intensity = heavy ? 2.4f : 1.3f,
            Glow = heavy ? 0.25f : 0.1f
        }, heavy ? 0.35f : 0.2f);

        if (heavy)
        {
            Spray(_dust, position, Vector2.UnitY, MathF.Tau, 40, 30, 160);
            Shockwave(position - Vector2.UnitY * 40, 110, 260);
        }
    }

    /// <summary>
    /// A kick was blocked, the guard only gives off a pale puff back at the attacker.
    /// </summary>
    /// <param name="direction">The way the kick was going, -1 for left and 1 for right.</param>
    public void Block(Vector2 position, float direction)
    {
        Spray(_dust, position, new Vector2(-direction, 0.3f), 1.0f, 24, 60, 220);
    }

    /// <summary>
    /// Dust kicked up along the ground by a jump or a roll.
    /// </summary>
    /// <param name="direction">The way the dust flies, -1 for left, 1 for right and 0 for both.</param>
    public void Dust(Vector2 position, float direction, int count = 10)
    {
        if (direction != 0)
        {
            Spray(_dust, position, new Vector2(direction, 0.35f), 0.7f, count, 30, 150);
            return;
        }

        Spray(_dust, position, new Vector2(-1, 0.35f), 0.7f, count / 2, 30, 150);
        Spray(_dust, position, new Vector2(1, 0.35f), 0.7f, count / 2, 30, 150);
    }

    /// <summary>
    /// Dust thrown to both sides by a landing, the longer the fall the bigger the cloud.
    /// </summary>
    public void Land(Vector2 position, float fallDuration)
    {
        Dust(position, 0, Math.Clamp((int)(fallDuration * 80), 8, 48));
        Shockwave(position, Math.Clamp(fallDuration * 220, 50, 110), Math.Clamp(fallDuration * 500, 90, 240));
    }

    /// <summary>
    /// The haze around the head of a stunned player, has to be called every frame for as long as the stun lasts.
    /// </summary>
    public void Haze(Vector2 position)
    {
        const float radius = 18f;
        Span<Particle2D> particles = stackalloc Particle2D[3];

        for (int i = 0; i < particles.Length; i++)
        {
            // Spread over the head rather than coming out of a single point, drifting whichever way
            var (sin, cos) = MathF.SinCos(Random.Shared.NextSingle() * MathF.Tau);
            Vector2 offset = new Vector2(cos, sin * 0.6f) * (Random.Shared.NextSingle() * radius);

            particles[i] = new Particle2D(new Vector2(-sin, cos), position + offset, 6 + Random.Shared.NextSingle() * 16);
        }

        _haze.AddRange(particles);
    }

    public override void UpdateState(float dt)
    {
        const float spawnInterval = 1.0f / 14.0f;

        _ambienceTimer += dt;
        while (_ambienceTimer >= spawnInterval)
        {
            _ambienceTimer -= spawnInterval;
            SpawnAmbience();
        }

        foreach (var emitter in _emitters)
        {
            // However many are due by now, so the rate holds no matter how long the frame was
            emitter.Timer += dt;
            int due = (int)(emitter.Timer * emitter.Rate);
            if (due < 1) continue;

            emitter.Timer -= due / emitter.Rate;
            Spray(emitter.Preset.Renderer, emitter.Position, -Vector2.UnitY, emitter.Preset.Spread, due, emitter.Preset.MinSpeed, emitter.Preset.MaxSpeed);
        }

        base.UpdateState(dt);
    }

    private void SpawnAmbience()
    {
        // Spawns over the top or from underneath the view dynamically based on gravity definition
        var view = _camera.Bounds;
        float x = view.X - AMBIENCE_MARGIN + Random.Shared.NextSingle() * (view.Width + AMBIENCE_MARGIN * 2);
        float y = _rising ? view.Y - 8 : view.Y + view.Height + 8;

        float sway = Random.Shared.NextSingle() - 0.5f;
        Vector2 direction = Vector2.Normalize(_rising ? new Vector2(sway, 1) : new Vector2(sway - 0.4f, -1));

        _ambience.Add(new Particle2D(direction, new Vector2(x, y), 30 + Random.Shared.NextSingle() * 40));
    }

    /// <summary>
    /// Helper method to spray particles in a cone, each at its own speed so they don't fly out as one ring.
    /// </summary>
    private static void Spray(ParticleRenderer2D renderer, Vector2 position, Vector2 direction, float spread, int count, float minSpeed, float maxSpeed)
    {
        float centre = MathF.Atan2(direction.Y, direction.X);
        Span<Particle2D> particles = stackalloc Particle2D[Math.Min(count, MaxSprayCount)];

        for (int i = 0; i < particles.Length; i++)
        {
            float angle = centre + (Random.Shared.NextSingle() - 0.5f) * spread;
            var (sin, cos) = MathF.SinCos(angle);

            particles[i] = new Particle2D(new Vector2(cos, sin), position, minSpeed + Random.Shared.NextSingle() * (maxSpeed - minSpeed));
        }

        renderer.AddRange(particles);
    }
}