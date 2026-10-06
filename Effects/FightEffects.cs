using System;
using System.Numerics;

using Horizon.Engine;
using Horizon.Physics;
using Horizon.Physics.Simulation;
using Horizon.Rendering;
using Horizon.Rendering.Particles;

namespace Fighter2D.Effects;

/// <summary>
/// Every particle effect of a fight. The blood and dust the players throw up, and whatever the emitters of the map leak.
/// The rest of the game only says what happened (a hit, a landing etc.), how that looks is decided here.
/// What is in the air of the map by itself (rain, a storm) is the <see cref="Weather"/>.
/// </summary>
internal class FightEffects : GameObject
{
    // How hard each kind of hit shakes the screen, in pixels of the art
    private const float HIT_SHAKE = 2.5f;
    private const float HEAVY_HIT_SHAKE = 5.0f;
    private const float BLOCK_SHAKE = 1.5f;

    // How long (in seconds) the shake of a hit takes to die away
    private const float SHAKE_TIME = 0.28f;

    private readonly FightParticles _particles;
    private readonly MapEmitters _emitters;
    private readonly PhysicsWorld _world;
    private readonly ScreenShake _shake = new();

    /// <summary>
    /// How far a hit that just landed has the view off from where it belongs. Zero most of the time.
    /// </summary>
    public Vector2 Shake => _shake.Offset;

    /// <param name="lighting">The renderer the fight is lit by, glowing emitters add their lights to it.</param>
    public FightEffects(PhysicsWorld world, DeferredRenderer2D lighting)
    {
        Name = "Fight Effects";

        _world = world;
        _particles = new FightParticles(this, world);
        _emitters = new MapEmitters(_particles, lighting);
    }

    /// <summary>
    /// Places an emitter of the map, see <see cref="MapEmitters.Add"/>.
    /// </summary>
    public bool AddEmitter(string kind, Vector2 position, float rate) => _emitters.Add(kind, position, rate);

    /// <summary>
    /// Blows whatever is lying around a spot (rain on the floor etc.) away from it, for the heavier moves.
    /// The players themselves are left alone, shoving them about is the job of the moves.
    /// </summary>
    /// <param name="position">Where on the ground the move happened.</param>
    public void Shockwave(Vector2 position, float radius, float strength)
    {
        // From slightly under the ground, so what lies on it gets thrown up rather than pushed along it
        _world.ApplyRadialImpulse(position - Vector2.UnitY * 12, radius, strength, PhysicsRadialTargets.Particles);
    }

    /// <summary>
    /// A hit has landed and draws blood. Heavy hits (launchers and counter hits) draw a lot more of it.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    public void Hit(Vector2 position, float direction, bool heavy)
    {
        // The hit itself is a burst of bright lines in every direction and a shake of the screen
        ParticleSpray.Cone(_particles.Impact, position, new Vector2(direction, 0.0f), MathF.Tau, heavy ? 28 : 14, 280, heavy ? 760 : 540);
        _shake.Kick(heavy ? HEAVY_HIT_SHAKE : HIT_SHAKE, SHAKE_TIME);

        // Most of the blood goes the way the hit went, in an arc that comes down a good way behind the victim
        ParticleSpray.Cone(_particles.Blood, position, new Vector2(direction, 0.45f), 1.1f, heavy ? 45 : 16, 90, heavy ? 520 : 340);

        // Some comes straight back at whoever threw it
        ParticleSpray.Cone(_particles.Blood, position, new Vector2(-direction, 0.6f), 1.6f, heavy ? 22 : 8, 40, 180);

        // And the fine mist, which goes everywhere and nowhere far
        ParticleSpray.Cone(_particles.BloodMist, position, new Vector2(direction, 0.2f), 2.6f, heavy ? 160 : 106, 20, 170, 3.0f);

        if (!heavy) return;

        // A hit that lifts somebody off their feet takes some of it along upwards
        ParticleSpray.Cone(_particles.Blood, position, new Vector2(direction * 0.35f, 1.0f), 0.9f, 42, 200, 500);
        Shockwave(position - Vector2.UnitY * 40, 110, 260);
    }

    /// <summary>
    /// A hit was blocked, which only gives off a pale puff back at the attacker.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    public void Block(Vector2 position, float direction)
    {
        ParticleSpray.Cone(_particles.Impact, position, new Vector2(-direction, 0.3f), 1.8f, 8, 200, 420);
        ParticleSpray.Cone(_particles.Dust, position, new Vector2(-direction, 0.3f), 1.0f, 24, 60, 220);
        _shake.Kick(BLOCK_SHAKE, SHAKE_TIME);
    }

    /// <summary>
    /// Dust kicked up along the ground by a jump or a roll.
    /// </summary>
    /// <param name="direction">The way the dust flies, -1 for left, 1 for right and 0 for both.</param>
    public void Dust(Vector2 position, float direction, int count = 10)
    {
        if (direction != 0)
        {
            ParticleSpray.Cone(_particles.Dust, position, new Vector2(direction, 0.35f), 0.7f, count, 30, 150);
            return;
        }

        ParticleSpray.Cone(_particles.Dust, position, new Vector2(-1, 0.35f), 0.7f, count / 2, 30, 150);
        ParticleSpray.Cone(_particles.Dust, position, new Vector2(1, 0.35f), 0.7f, count / 2, 30, 150);
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
    /// The haze around the head of a player in hitstun, has to be called every frame for as long as it lasts.
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

        _particles.Haze.AddRange(particles);
    }

    public override void UpdateState(float dt)
    {
        _shake.Update(dt);
        _emitters.Update(dt);

        base.UpdateState(dt);
    }
}
