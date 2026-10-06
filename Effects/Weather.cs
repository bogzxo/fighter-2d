using System;
using System.Numerics;

using Horizon.Engine;
using Horizon.Physics;
using Horizon.Physics.Simulation;
using Horizon.Rendering;
using Horizon.Rendering.Lighting;
using Horizon.Rendering.Particles;

namespace Fighter2D.Effects;

/// <summary>
/// What the environment is doing out of the definition of the map (maps.hor)
/// </summary>
internal sealed class Weather : GameObject
{
    private const float SPAWN_MARGIN = 96f;       // How far outside of the view the particles spawn, so they never pop in

    // How quickly what falls is back at the speed it falls at after something knocked it about
    private const float FALL_RATE = 4.0f;

    private const float FLASH_FADE = 9.0f;        // How quickly a flash of lightning dies away, the higher the quicker
    private const float FLASH_LIGHT_TIME = 0.3f;  // How long (in seconds) the light a bolt throws lasts
    private const float FLICKER_STRENGTH = 0.6f;  // How bright the second flash of a bolt is against the first
    private const float RUMBLE_FADE = 2.4f;       // How quickly the thunder dies away
    private const float RUMBLE_SPEED = 34.0f;     // How fast the thunder rattles the view

    private readonly ParticleRenderer2D _particles;
    private readonly Camera2D _camera;
    private readonly DeferredRenderer2D _lighting;
    private readonly MapLoader.AmbienceDefinition _ambience;
    private readonly MapLoader.StormDefinition _storm;

    // The light the map has when nothing is flashing
    private readonly Vector3 _ambient;

    // The speed the particles are let go at, null for the ones that only drift off under their gravity
    private readonly Vector2? _fall;
    private readonly bool _rising;
    private float _spawnTimer;

    // The storm: how long until the next bolt, how bright the flash still is, when its second flash and its thunder are due
    private float _untilBolt, _flash, _untilFlicker = -1.0f, _untilThunder = -1.0f, _rumble, _rumbleTime;

    /// <summary>
    /// How far the thunder has the view off of where it belongs right now, in whole pixels of the art. Zero most of the time.
    /// </summary>
    public Vector2 Shake { get; private set; }

    /// <param name="camera">The camera the fight is seen through, the particles are kept around it.</param>
    /// <param name="lighting">The renderer the fight is lit by, which is what the lightning lights up.</param>
    public Weather(Camera2D camera, MapLoader.AmbienceDefinition ambience, MapLoader.StormDefinition storm, PhysicsWorld world, DeferredRenderer2D lighting)
    {
        Name = "Weather";

        _camera = camera;
        _lighting = lighting;
        _ambience = ambience;
        _storm = storm;
        _ambient = lighting.Ambient;
        _fall = ambience.Velocity;

        // Whether they come in over the top of the view or from underneath it
        _rising = (_fall ?? ambience.Gravity).Y > 0;

        // These collide with the physics world (on the GPU), so they land on the map and the players can kick them about
        var simulator = new PhysicsParticleSimulator2D(world);
        simulator.Radius = ambience.Size;
        simulator.Restitution = 0.25f;
        simulator.Friction = 5.0f;
        simulator.Splash = ambience.Splash;
        simulator.SplashFade = ambience.SplashFade;

        // They are only wafted aside (by a player or a shockwave) at about the speed they drift at
        simulator.Mass = 3.0f;
        simulator.BodyPushLimit = 70.0f;

        Vector2 gravity = ambience.Gravity;
        if (_fall is Vector2 fall)
        {
            // Rain doesn't speed up on its way down, it has been falling for a long time before it gets here:
            // it is kept at the speed it is let go at for as long as it is in the air. What pulls on it is only
            // there to keep it on the ground once it is down
            simulator.Cruise = fall;
            simulator.CruiseRate = FALL_RATE;
            gravity = new Vector2(0.0f, fall.Y);
        }

        _particles = AddEntity(new ParticleRenderer2D(8192, simulator)
        {
            StartColor = ambience.StartColor,
            EndColor = ambience.EndColor,
            ParticleSize = ambience.Size,
            Stretch = ambience.Stretch,
            MaxAge = ambience.Lifetime,
            Gravity = gravity
        });

        ScheduleBolt();
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

        if (_storm.Enabled) UpdateStorm(dt);

        base.UpdateState(dt);
    }

    private void Spawn()
    {
        var view = _camera.Bounds;
        float y = _rising ? view.Y - 8 : view.Y + view.Height + 8;

        if (_fall is Vector2 fall)
        {
            // What falls at a slant has to start off to the side to end up over the view: as far as it goes
            // sideways on its way across
            float slant = MathF.Abs(fall.Y) > 1.0f ? view.Height * fall.X / MathF.Abs(fall.Y) : 0.0f;
            float left = view.X - SPAWN_MARGIN - MathF.Max(slant, 0.0f);
            float right = view.X + view.Width + SPAWN_MARGIN - MathF.Min(slant, 0.0f);

            // At the speed it falls at from the start, a little faster or slower from drop to drop
            float speed = 0.85f + Random.Shared.NextSingle() * 0.3f;
            _particles.Add(new Particle2D(fall, new Vector2(left + Random.Shared.NextSingle() * (right - left), y), speed, Steady: true));
            return;
        }

        // Petals sink on the wind and embers rise, slowly at first
        float x = view.X - SPAWN_MARGIN + Random.Shared.NextSingle() * (view.Width + SPAWN_MARGIN * 2);
        float sway = Random.Shared.NextSingle() - 0.5f;
        Vector2 direction = Vector2.Normalize(_rising ? new Vector2(sway, 1) : new Vector2(sway - 0.4f, -1));

        _particles.Add(new Particle2D(direction, new Vector2(x, y), 30 + Random.Shared.NextSingle() * 40));
    }

    private void UpdateStorm(float dt)
    {
        _untilBolt -= dt;
        if (_untilBolt <= 0.0f)
        {
            Strike(1.0f);
            ScheduleBolt();

            // A bolt flashes twice, and is heard a moment after it is seen
            _untilFlicker = 0.08f + Random.Shared.NextSingle() * 0.1f;
            _untilThunder = 0.35f + Random.Shared.NextSingle() * 0.8f;
        }

        if (_untilFlicker >= 0.0f && (_untilFlicker -= dt) < 0.0f) Strike(FLICKER_STRENGTH);

        if (_untilThunder >= 0.0f && (_untilThunder -= dt) < 0.0f)
        {
            _rumble = 1.0f;
            _rumbleTime = 0.0f;
        }

        // The sky lights up everything at once, which is what the light of the map itself is for
        _flash *= MathF.Exp(-FLASH_FADE * dt);
        if (_flash < 0.005f) _flash = 0.0f;
        _lighting.Ambient = _ambient + _storm.Color * (_storm.Brightness * _flash);

        // And the thunder rattles the view, a pixel or two and less and less
        _rumble *= MathF.Exp(-RUMBLE_FADE * dt);
        _rumbleTime += dt;

        float reach = _storm.Shake * _rumble;
        Shake = reach < 0.5f ? Vector2.Zero : new Vector2(
            MathF.Round(MathF.Sin(_rumbleTime * RUMBLE_SPEED) * reach),
            MathF.Round(MathF.Sin(_rumbleTime * RUMBLE_SPEED * 1.37f + 1.3f) * reach));

    }

    /// <summary>
    /// Helper method to flash the sky: everything is lit up for a moment, and a light from where the bolt came down
    /// throws the shadows that go with it.
    /// </summary>
    private void Strike(float strength)
    {
        _flash = MathF.Max(_flash, strength);

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

    private void ScheduleBolt()
    {
        _untilBolt = _storm.MinInterval + Random.Shared.NextSingle() * MathF.Max(0.0f, _storm.MaxInterval - _storm.MinInterval);
    }
}
