using System;
using System.Numerics;

using Fighter2D.Map;

using Horizon.Engine;
using Horizon.Physics;
using Horizon.Physics.Simulation;
using Horizon.Rendering;
using Horizon.Rendering.Particles;

namespace Fighter2D.Effects;

/// <summary>
/// Whatever is in the air of a map, going by its definition in maps.hor. That is the rain, petals or embers, and the thunderstorm if it has one.
/// </summary>
internal sealed class Weather : GameObject
{
    // How far outside of the view the particles spawn, so they never pop in
    private const float SPAWN_MARGIN = 96f;

    // How quickly rain is back at its falling speed after something knocked it about
    private const float FALL_RATE = 4.0f;

    private readonly ParticleRenderer2D _particles;
    private readonly Camera2D _camera;
    private readonly AmbienceDefinition _ambience;
    private readonly Storm? _storm;

    // The speed the particles are let go at, null for the ones that only drift off under their gravity
    private readonly Vector2? _fall;

    // Whether they come in from under the view (embers) instead of over the top of it
    private readonly bool _rising;
    private float _spawnTimer;

    /// <summary>
    /// How far the thunder has the view off from where it belongs. Zero most of the time.
    /// </summary>
    public Vector2 Shake => _storm?.Shake ?? Vector2.Zero;

    /// <param name="camera">The camera the fight is seen through, the particles are kept around it.</param>
    /// <param name="lighting">The renderer the fight is lit by, which is what the lightning lights up.</param>
    public Weather(Camera2D camera, MapDefinition map, PhysicsWorld world, DeferredRenderer2D lighting)
    {
        Name = "Weather";

        _camera = camera;
        _ambience = map.Ambience;
        _fall = _ambience.Velocity;
        _rising = (_fall ?? _ambience.Gravity).Y > 0;

        _particles = AddEntity(CreateParticles(world));

        if (map.Storm.Enabled) _storm = new Storm(map.Storm, camera, lighting);
    }

    private ParticleRenderer2D CreateParticles(PhysicsWorld world)
    {
        // These collide with the physics world (on the GPU), so they land on the map and the players can kick them about
        var simulator = new PhysicsParticleSimulator2D(world);
        simulator.Radius = _ambience.Size;
        simulator.Restitution = 0.25f;
        simulator.Friction = 5.0f;
        simulator.Splash = _ambience.Splash;
        simulator.SplashFade = _ambience.SplashFade;

        // A player or a shockwave only wafts them aside at about the speed they drift at
        simulator.Mass = 3.0f;
        simulator.BodyPushLimit = 70.0f;

        Vector2 gravity = _ambience.Gravity;
        if (_fall is Vector2 fall)
        {
            // Rain doesn't speed up on its way down, it has been falling for ages before it gets here.
            // So it is held at the speed it is let go at, and gravity is only there to keep it on the ground once it is down
            simulator.Cruise = fall;
            simulator.CruiseRate = FALL_RATE;
            gravity = new Vector2(0.0f, fall.Y);
        }

        return new ParticleRenderer2D(8192, simulator)
        {
            StartColor = _ambience.StartColor,
            EndColor = _ambience.EndColor,
            ParticleSize = _ambience.Size,
            Stretch = _ambience.Stretch,
            MaxAge = _ambience.Lifetime,
            Gravity = gravity
        };
    }

    public override void UpdateState(float dt)
    {
        if (_ambience.Rate > 0.0f)
        {
            // However many are due by now, so the rate holds no matter how long the frame was
            _spawnTimer += dt;
            int due = (int)(_spawnTimer * _ambience.Rate);
            _spawnTimer -= due / _ambience.Rate;

            for (int i = 0; i < due; i++) Spawn();
        }

        _storm?.Update(dt);

        base.UpdateState(dt);
    }

    private void Spawn()
    {
        var view = _camera.Bounds;
        float y = _rising ? view.Y - 8 : view.Y + view.Height + 8;

        if (_fall is Vector2 fall)
        {
            SpawnFalling(fall, view.X, view.Width, view.Height, y);
            return;
        }

        // Petals sink on the wind and embers rise, slowly at first
        float x = view.X - SPAWN_MARGIN + Random.Shared.NextSingle() * (view.Width + SPAWN_MARGIN * 2);
        float sway = Random.Shared.NextSingle() - 0.5f;
        Vector2 direction = Vector2.Normalize(_rising ? new Vector2(sway, 1) : new Vector2(sway - 0.4f, -1));

        _particles.Add(new Particle2D(direction, new Vector2(x, y), 30 + Random.Shared.NextSingle() * 40));
    }

    /// <summary>
    /// Helper method to spawn a drop of rain, which goes at its falling speed from the start.
    /// </summary>
    private void SpawnFalling(Vector2 fall, float viewX, float viewWidth, float viewHeight, float y)
    {
        // Rain that falls at a slant has to start off to the side to end up over the view
        float slant = MathF.Abs(fall.Y) > 1.0f ? viewHeight * fall.X / MathF.Abs(fall.Y) : 0.0f;
        float left = viewX - SPAWN_MARGIN - MathF.Max(slant, 0.0f);
        float right = viewX + viewWidth + SPAWN_MARGIN - MathF.Min(slant, 0.0f);

        // A little faster or slower from drop to drop
        float speed = 0.85f + Random.Shared.NextSingle() * 0.3f;
        _particles.Add(new Particle2D(fall, new Vector2(left + Random.Shared.NextSingle() * (right - left), y), speed, Steady: true));
    }
}
