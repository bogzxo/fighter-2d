using System.Numerics;

using Horizon.Engine;
using Horizon.Physics;
using Horizon.Physics.Simulation;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Particles.Simulation;

namespace Fighter2D.Effects;

/// <summary>
/// Every kind of particle a fight uses and how each one looks and behaves. Nothing in here decides when they fly, only what they are.
/// To add a new kind give it a renderer here and let <see cref="FightEffects"/> or <see cref="MapEmitters"/> spray it.
/// </summary>
internal sealed class FightParticles
{
    // Drops of water are drawn a touch bigger than the room they take up, or a pool would look like a grid of dots
    private const float WATER_OVERLAP = 1.1f;

    // How far (in seconds of its way) a falling drop of water is stretched, which is what closes the gaps in a stream
    private const float WATER_STRETCH = 0.13f;
    private const float WATER_MAX_STRETCH = 40.0f;

    // Blood only gets a touch of that, a flying drop is a streak and a landed one is a dot
    private const float BLOOD_STRETCH = 0.05f;

    public readonly ParticleRenderer2D Water, Lava, Blood, Impact, BloodMist, Dust, Haze;

    /// <param name="owner">Whoever the renderers get added to. The order they are added in is the order they are drawn in.</param>
    public FightParticles(GameObject owner, PhysicsWorld world)
    {
        Water = owner.AddEntity(CreateWater(world));
        Lava = owner.AddEntity(CreateLava(world));
        Blood = owner.AddEntity(CreateBlood(world));
        Impact = owner.AddEntity(CreateImpact());
        BloodMist = owner.AddEntity(CreateBloodMist());
        Dust = owner.AddEntity(CreateDust());
        Haze = owner.AddEntity(CreateHaze());
    }

    /// <summary>
    /// Water collides with itself, so it pools where it lands and spills over once the pool is full. Runny, it levels out quickly.
    /// </summary>
    private static ParticleRenderer2D CreateWater(PhysicsWorld world)
    {
        var simulator = new PhysicsFluidParticleSimulator2D(world);
        simulator.Particles.Radius = 2.5f;
        simulator.Particles.Friction = 1.0f;

        // The emitters let twice as much out as a map asks for, so it lives half as long. The basins of the maps were made for that much water
        simulator.MaxLife = 8;

        // Enough for a splash when somebody drops into it, wading through only parts it
        simulator.Particles.BodyPush = 0.05f;
        simulator.Particles.BodyPushLimit = 110.0f;

        return new ParticleRenderer2D(8192 * 2, simulator)
        {
            StartColor = new Vector3(3 / 255.0f, 98 / 255.0f, 252 / 255.0f),
            EndColor = new Vector3(3 / 255.0f, 169 / 255.0f, 252 / 255.0f),
            ParticleSize = simulator.Particles.Radius * WATER_OVERLAP,
            Stretch = WATER_STRETCH,
            MaxStretch = WATER_MAX_STRETCH,
            MaxAge = 9.0f,
            Gravity = new Vector2(0, -300)
        };
    }

    /// <summary>
    /// Lava is thick. It heaps up, creeps outwards and cools from a glow into a dark crust.
    /// </summary>
    private static ParticleRenderer2D CreateLava(PhysicsWorld world)
    {
        var simulator = new PhysicsFluidParticleSimulator2D(world);
        simulator.Particles.Radius = 2.5f;
        simulator.Particles.Friction = 14.0f;
        simulator.Particles.LinearDrag = 1.5f;
        simulator.Particles.Mass = 3.0f;
        simulator.Particles.BodyPush = 0.05f;
        simulator.Particles.BodyPushLimit = 60.0f;
        simulator.MaxLife = 15;

        return new ParticleRenderer2D(8192, simulator)
        {
            StartColor = new Vector3(1.0f, 0.62f, 0.1f),
            EndColor = new Vector3(0.35f, 0.04f, 0.0f),
            ParticleSize = 2.5f,
            MaxAge = 14.0f,
            Gravity = new Vector2(0, -180),

            // It is its own light until it has cooled down
            StartEmissive = 0.45f,
            EndEmissive = 0.15f
        };
    }

    /// <summary>
    /// Heavy drops that arc away from a hit, stay where they land and dry darker until they are gone.
    /// </summary>
    private static ParticleRenderer2D CreateBlood(PhysicsWorld world)
    {
        // Blood doesn't bounce and doesn't slide, where a drop comes down is where it stays
        var simulator = new PhysicsParticleSimulator2D(world);
        simulator.Radius = 1.5f;
        simulator.Restitution = 0.04f;
        simulator.Friction = 22.0f;
        simulator.LinearDrag = 0.5f;

        // Weightless as far as shoves go, otherwise the shockwave of the hit it came from would blow it all away
        simulator.Mass = 0.0f;

        return new ParticleRenderer2D(8192, simulator)
        {
            // The end colour is drawn at twice what it says here, see the shader of the particles
            StartColor = new Vector3(0.74f, 0.02f, 0.04f),
            EndColor = new Vector3(0.14f, 0.0f, 0.01f),
            ParticleSize = 1.5f,
            Stretch = BLOOD_STRETCH,
            MaxAge = 3.2f,
            Gravity = new Vector2(0, -1100),

            // Visible on a dark map without glowing on a bright one
            StartEmissive = 0.3f,
            EndEmissive = 0.1f
        };
    }

    /// <summary>
    /// The bright lines that burst out of the spot a hit lands on.
    /// </summary>
    private static ParticleRenderer2D CreateImpact() => new(2048, new ComputeParticleSimulator2D())
    {
        StartColor = new Vector3(1.0f, 0.97f, 0.88f),
        EndColor = new Vector3(0.5f, 0.32f, 0.12f),
        ParticleSize = 1.0f,
        Stretch = 0.05f,
        MaxStretch = 30.0f,
        MaxAge = 0.16f,
        Emissive = 1.0f
    };

    /// <summary>
    /// The fine red spray that hangs in the air for a moment where a hit landed.
    /// </summary>
    private static ParticleRenderer2D CreateBloodMist() => new(4096, new ComputeParticleSimulator2D())
    {
        StartColor = new Vector3(0.85f, 0.05f, 0.07f),
        EndColor = new Vector3(0.2f, 0.0f, 0.01f),
        ParticleSize = 1.0f,
        MaxAge = 0.5f,
        Gravity = new Vector2(0, -260),
        Emissive = 0.3f
    };

    /// <summary>
    /// Pale and slow, it hangs around the feet.
    /// </summary>
    private static ParticleRenderer2D CreateDust() => new(8192, new ComputeParticleSimulator2D())
    {
        StartColor = new Vector3(0.85f, 0.82f, 0.75f),
        EndColor = new Vector3(0.3f, 0.3f, 0.3f),
        ParticleSize = 1.5f,
        MaxAge = 0.45f,

        // Down, and properly. It used to be 60 the other way, which had every puff drifting up past the head of whoever
        // kicked it up like there was no gravity at all. Dust hops off the floor and is back on it before it is gone
        Gravity = new Vector2(0, -420)
    };

    /// <summary>
    /// The purple cloud around the head of somebody in hitstun.
    /// </summary>
    private static ParticleRenderer2D CreateHaze() => new(4096, new ComputeParticleSimulator2D())
    {
        StartColor = new Vector3(0.8f, 0.7f, 1.0f),
        EndColor = new Vector3(0.3f, 0.2f, 0.5f),
        ParticleSize = 2.0f,
        MaxAge = 0.9f,
        Gravity = new Vector2(0, 30),

        // Somebody in hitstun has to be seen to be, however dark the map is
        Emissive = 0.7f
    };
}
