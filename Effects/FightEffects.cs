using System;
using System.Numerics;

using Horizon.Engine;
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

    private readonly ParticleRenderer2D _sparks, _dust, _haze, _ambience;
    private readonly Camera2D _camera;
    private readonly bool _embers;
    private float _ambienceTimer = 0.0f;

    /// <param name="camera">The camera the fight is seen through, the ambience is kept around it.</param>
    /// <param name="ambience">The ambience of the map as named in maps.hor ("petals" or "embers").</param>
    public FightEffects(Camera2D camera, string ambience)
    {
        Name = "Fight Effects";

        _camera = camera;
        _embers = ambience == "embers";

        // Hot and short lived, they arc away from a hit
        _sparks = AddEntity(new ParticleRenderer2D(8192, new ComputeParticleSimulator2D())
        {
            StartColor = new Vector3(1.0f, 0.95f, 0.6f),
            EndColor = new Vector3(0.5f, 0.1f, 0.0f),
            ParticleSize = 1.5f,
            MaxAge = 0.45f,
            Gravity = new Vector2(0, -900)
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
            Gravity = new Vector2(0, 30)
        });

        // Petals sink on the wind, embers rise
        _ambience = AddEntity(new ParticleRenderer2D(4096, new ComputeParticleSimulator2D())
        {
            StartColor = _embers ? new Vector3(1.0f, 0.6f, 0.2f) : new Vector3(1.0f, 0.75f, 0.85f),
            EndColor = _embers ? new Vector3(0.4f, 0.05f, 0.0f) : new Vector3(0.5f, 0.3f, 0.4f),
            ParticleSize = 1.5f,
            MaxAge = 9.0f,
            Gravity = _embers ? new Vector2(4, 12) : new Vector2(-12, -6)
        });
    }

    /// <summary>
    /// A kick has landed, heavy hits (the ones that launch) throw a lot more sparks.
    /// </summary>
    /// <param name="direction">The way the kick was going, -1 for left and 1 for right.</param>
    public void Hit(Vector2 position, float direction, bool heavy)
    {
        Spray(_sparks, position, new Vector2(direction, 0.5f), 1.4f, heavy ? 110 : 40, 60, heavy ? 480 : 300);

        if (heavy)
        {
            Spray(_dust, position, Vector2.UnitY, MathF.Tau, 40, 30, 160);
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

        base.UpdateState(dt);
    }

    private void SpawnAmbience()
    {
        // Petals come in over the top of the view and embers from underneath it
        var view = _camera.Bounds;
        float x = view.X - AMBIENCE_MARGIN + Random.Shared.NextSingle() * (view.Width + AMBIENCE_MARGIN * 2);
        float y = _embers ? view.Y - 8 : view.Y + view.Height + 8;

        float sway = Random.Shared.NextSingle() - 0.5f;
        Vector2 direction = Vector2.Normalize(_embers ? new Vector2(sway, 1) : new Vector2(sway - 0.4f, -1));

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
