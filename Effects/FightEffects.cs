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
/// Every particle effect of a fight: the blood and dust thrown up by the players, and what the emitters of the map let go.
/// What is in the air of the map by itself (rain, a storm) is the <see cref="Weather"/>.
/// The controllers and move routines only say what happened (a hit, a landing etc.), how it looks is decided here.
/// </summary>
internal class FightEffects : GameObject
{
    private const int MaxSprayCount = 128;
    private const float EMITTER_LIGHT_DROP = 24f; // How far underneath an emitter the light of what it lets go is

    // How much bigger than the room a drop of water takes up it is drawn, so the drops of a pool leave no gaps between them
    private const float WATER_OVERLAP = 1.1f;

    // How far (in seconds of its way) a drop of water is drawn out as it falls, which is what closes the gaps of a
    // stream: there is a bit less than this long between two drops that fall one behind the other.
    // And the longest that makes it, in units of the world
    private const float WATER_STRETCH = 0.13f;
    private const float WATER_MAX_STRETCH = 40.0f;

    // Blood only a touch, a drop that flies is a streak and one that has landed is a dot
    private const float BLOOD_STRETCH = 0.05f;

    // How quickly the jolt of a blow dies away, and how fast it rattles the view
    private const float JOLT_FADE = 14.0f;
    private const float JOLT_SPEED = 55.0f;

    private float _jolt, _joltTime;

    /// <summary>
    /// How far a blow that just landed has the view off of where it belongs right now, in whole pixels of the art. Zero most of the time.
    /// </summary>
    public Vector2 Shake { get; private set; }

    private readonly ParticleRenderer2D _impact, _blood, _bloodMist, _dust, _haze, _water, _lava;
    private readonly PhysicsWorld _world;
    private readonly DeferredRenderer2D _lighting;

    // How an emitter of a kind lets its particles go, they all fall out of it downwards.
    // The ones that let something glowing go come with a light as well, which every emitter gets a copy of.
    // Density is how many particles one of the rate a map gives an emitter stands for and Width how far to either
    // side of it they come out, for a stream that is more than one drop wide
    private readonly record struct EmitterPreset(ParticleRenderer2D Renderer, float Spread, float MinSpeed, float MaxSpeed, Light2D? Light = null, float Density = 1.0f, float Width = 0.0f);

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


    /// <param name="lighting">The renderer the fight is lit by, which the flashes and the glow of what is hot are added to.</param>
    public FightEffects(PhysicsWorld world, DeferredRenderer2D lighting)
    {
        Name = "Fight Effects";

        _world = world;
        _lighting = lighting;

        
        // Blood doesn't bounce and doesn't slide: where a drop comes down is where it stays
        var bloodSimulator = new PhysicsParticleSimulator2D(world);
        bloodSimulator.Radius = 1.5f;
        bloodSimulator.Restitution = 0.04f;
        bloodSimulator.Friction = 22.0f;
        bloodSimulator.LinearDrag = 0.5f;

        // Weightless as far as pushes go: the shockwave of the hit it comes from would only blow it all away
        bloodSimulator.Mass = 0.0f;

        // The liquids collide with themselves too, so they pool where they land and overflow once that is full.
        // Runny, it levels out quickly
        var waterSimulator = new PhysicsFluidParticleSimulator2D(world);
        waterSimulator.Particles.Radius = 2.5f;
        waterSimulator.Particles.Friction = 1.0f;

        // Twice what a map asks for comes out (see the emitter presets), so it stays half as long: the basins of the
        // maps were made for that much water
        waterSimulator.MaxLife = 8;

        // Enough for a splash when someone drops into it, wading through only parts it
        waterSimulator.Particles.BodyPush = 0.05f;
        waterSimulator.Particles.BodyPushLimit = 110.0f;

        // Thick, it heaps up and only creeps outwards once it is down
        var lavaSimulator = new PhysicsFluidParticleSimulator2D(world);
        lavaSimulator.Particles.Radius = 2.5f;
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
            // A touch bigger than the room a drop takes up, or a pool would be a grid of dots
            ParticleSize = waterSimulator.Particles.Radius * WATER_OVERLAP,
            Stretch = WATER_STRETCH,
            MaxStretch = WATER_MAX_STRETCH,
            MaxAge = 9.0f,
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
            StartEmissive = 0.45f,
            EndEmissive = 0.15f
        });

        // The names are the ones a map gives its emitters ("emitter_type")
        _emitterPresets = new(StringComparer.OrdinalIgnoreCase)
        {
            // Two drops wide, and no more of them than fit out of there side by side: let go on top of one another
            // they only burst apart. What keeps it a stream all the way down is how they are drawn out as they fall
            ["water"] = new EmitterPreset(_water, 0.2f, 40, 60, Density: 2.0f, Width: 2.5f),
            ["lava"] = new EmitterPreset(_lava, 0.25f, 4, 18, new Light2D
            {
                Color = new Vector3(1.0f, 0.45f, 0.12f),
                Radius = 150.0f,
                Intensity = 0.6f,
                Glow = 0.22f,
                Flicker = 0.25f,
                Size = 8.0f
            })
        };

        // Heavy drops that arc away from a hit and stay where they land, drying darker until they are gone.
        // The end colour is drawn at twice what it says here, see the shader of the particles
        _blood = AddEntity(new ParticleRenderer2D(8192, bloodSimulator)
        {
            StartColor = new Vector3(0.74f, 0.02f, 0.04f),
            EndColor = new Vector3(0.14f, 0.0f, 0.01f),
            ParticleSize = 1.5f,
            Stretch = BLOOD_STRETCH,
            MaxAge = 3.2f,
            Gravity = new Vector2(0, -1100),

            // Seen for what it is in a dark map as well, without glowing in a bright one
            StartEmissive = 0.3f,
            EndEmissive = 0.1f
        });

        // The lines that burst out of where a blow lands
        _impact = AddEntity(new ParticleRenderer2D(2048, new ComputeParticleSimulator2D())
        {
            StartColor = new Vector3(1.0f, 0.97f, 0.88f),
            EndColor = new Vector3(0.5f, 0.32f, 0.12f),
            ParticleSize = 1.0f,
            Stretch = 0.05f,
            MaxStretch = 30.0f,
            MaxAge = 0.16f,
            Emissive = 1.0f
        });

        // The fine spray that hangs in the air for a moment where the blow landed
        _bloodMist = AddEntity(new ParticleRenderer2D(4096, new ComputeParticleSimulator2D())
        {
            StartColor = new Vector3(0.85f, 0.05f, 0.07f),
            EndColor = new Vector3(0.2f, 0.0f, 0.01f),
            ParticleSize = 1.0f,
            MaxAge = 0.5f,
            Gravity = new Vector2(0, -260),
            Emissive = 0.3f
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
    /// A blow has landed and draws blood, heavy hits (the ones that launch) a lot more of it.
    /// </summary>
    /// <param name="direction">The way the blow was going, -1 for left and 1 for right.</param>
    public void Hit(Vector2 position, float direction, bool heavy)
    {
        // The blow itself: a burst of lines every way, and the view jolted by it
        Spray(_impact, position, new Vector2(direction, 0.0f), MathF.Tau, heavy ? 28 : 14, 280, heavy ? 760 : 540);
        _jolt = MathF.Max(_jolt, heavy ? 5.0f : 2.5f);
        _joltTime = 0.0f;

        // Most of it goes the way the blow went, in an arc that comes down a good way behind whoever took it
        Spray(_blood, position, new Vector2(direction, 0.45f), 1.1f, heavy ? 45 : 16, 90, heavy ? 520 : 340);

        // Some comes straight back at whoever threw it
        Spray(_blood, position, new Vector2(-direction, 0.6f), 1.6f, heavy ? 22 : 8, 40, 180);

        // And the fine spray, which goes every way and nowhere far
        Spray(_bloodMist, position, new Vector2(direction, 0.2f), 2.6f, heavy ? 160 : 106, 20, 170, 3.0f);

        if (heavy)
        {
            // What a blow that lifts somebody off their feet takes along upwards
            Spray(_blood, position, new Vector2(direction * 0.35f, 1.0f), 0.9f, 42, 200, 500);

            //Spray(_dust, position, Vector2.UnitY, MathF.Tau, 40, 30, 160);
            Shockwave(position - Vector2.UnitY * 40, 110, 260);
        }
    }

    /// <summary>
    /// A kick was blocked, the guard only gives off a pale puff back at the attacker.
    /// </summary>
    /// <param name="direction">The way the kick was going, -1 for left and 1 for right.</param>
    public void Block(Vector2 position, float direction)
    {
        // A few lines back off the guard, and hardly a jolt
        Spray(_impact, position, new Vector2(-direction, 0.3f), 1.8f, 8, 200, 420);
        _jolt = MathF.Max(_jolt, 1.5f);
        _joltTime = 0.0f;

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
        // The jolt of a blow, a few pixels and gone within a couple of frames
        _jolt *= MathF.Exp(-JOLT_FADE * dt);
        _joltTime += dt;
        Shake = _jolt < 0.5f ? Vector2.Zero : new Vector2(
            MathF.Round(MathF.Sin(_joltTime * JOLT_SPEED) * _jolt),
            MathF.Round(MathF.Sin(_joltTime * JOLT_SPEED * 1.3f + 0.9f) * _jolt * 0.6f));

        foreach (var emitter in _emitters)
        {
            // However many are due by now, so the rate holds no matter how long the frame was
            float rate = emitter.Rate * emitter.Preset.Density;

            emitter.Timer += dt;
            int due = (int)(emitter.Timer * rate);
            if (due < 1) continue;

            emitter.Timer -= due / rate;
            Spray(emitter.Preset.Renderer, emitter.Position, -Vector2.UnitY, emitter.Preset.Spread, due, emitter.Preset.MinSpeed, emitter.Preset.MaxSpeed, emitter.Preset.Width);
        }

        base.UpdateState(dt);
    }

    /// <summary>
    /// Helper method to spray particles in a cone, each at its own speed so they don't fly out as one ring.
    /// </summary>
    /// <param name="scatter">How far from the position a particle can start, 0 for all of them from the one spot.</param>
    private static void Spray(ParticleRenderer2D renderer, Vector2 position, Vector2 direction, float spread, int count, float minSpeed, float maxSpeed, float scatter = 0.0f)
    {
        float centre = MathF.Atan2(direction.Y, direction.X);
        Span<Particle2D> particles = stackalloc Particle2D[Math.Min(count, MaxSprayCount)];

        for (int i = 0; i < particles.Length; i++)
        {
            float angle = centre + (Random.Shared.NextSingle() - 0.5f) * spread;
            var (sin, cos) = MathF.SinCos(angle);

            Vector2 start = position;
            if (scatter > 0.0f)
            {
                start += new Vector2(Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f) * (scatter * 2.0f);
            }

            particles[i] = new Particle2D(new Vector2(cos, sin), start, minSpeed + Random.Shared.NextSingle() * (maxSpeed - minSpeed));
        }

        renderer.AddRange(particles);
    }
}