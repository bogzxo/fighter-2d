using System;
using System.Numerics;

using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Physics;
using Horizon.Physics.Simulation;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Effects;

/// <summary>
/// Two fighters going at each other forever, for the background of the main menu.
/// Nobody plays them and nobody gets hurt (unfortunatly), they just loop through run up, kick, slide back and catch their breath.
/// They get a physics world of their own with nothing in it but the ground and the two of them, which is what the sparks land on.
/// </summary>
internal class MenuDuel : GameObject
{
    // Everything is in the units of the camera (pixels of the window)
    private const float FIGHTER_SIZE = 448;
    private const float GAP_FAR = 620;
    private const float GAP_NEAR = 170;

    private const string CHARACTER_DIRECTORY = "Assets/sprites/characters/the_man";

    // The ground the sparks land on, how far it reaches to either side of the middle and how thick it is
    private const float GROUND_REACH = 4000;
    private const float GROUND_DEPTH = 200;

    // How far into the kick (0 to 1) it connects
    private const float STRIKE_MOMENT = 0.35f;

    private static readonly Vector4 TintBlue = new(0.45f, 0.7f, 1.0f, 1.0f);
    private static readonly Vector4 TintRed = new(1.0f, 0.5f, 0.5f, 1.0f);

    /// <summary>
    /// The steps of the duel, in the order they follow each other.
    /// </summary>
    private enum DuelPhase
    {
        Approach,
        Strike,
        Recoil,
        Rest
    }

    // How long every step of the duel takes, in seconds
    private static readonly float[] PhaseDurations = [0.85f, 0.55f, 0.8f, 1.1f];

    /// <summary>
    /// Called the moment a kick lands, for whoever wants to shake along.
    /// </summary>
    public event Action? Struck;

    // Where the fighters meet (X) and the height of the ground they stand on (Y)
    private readonly Vector2 _center;

    private MenuFighter _fighterLeft = null!, _fighterRight = null!;
    private ParticleRenderer2D _dust = null!, _sparksBlue = null!, _sparksRed = null!;

    private DuelPhase _phase = DuelPhase.Rest;
    private float _phaseTime;
    private bool _leftAttacks, _struck;

    /// <param name="center">Where the fighters meet (X) and the ground they stand on (Y)</param>
    public MenuDuel(Vector2 center)
    {
        _center = center;
        Name = "Menu Duel";
    }

    public override void Initialize()
    {
        // A world where nothing pulls on the fighters (the duel puts them where they are), only the sparks fall
        var world = AddComponent<PhysicsWorld>();

        var ground = world.CreateBody(PhysicsBodySimulationType.Static, new Vector2(_center.X - GROUND_REACH, _center.Y - GROUND_DEPTH));
        ground.CreateRectangularFixture(Vector2.Zero, new Vector2(GROUND_REACH * 2, GROUND_DEPTH));

        var spriteBatch = AddEntity<SpriteBatch>();

        // The fighter on the left is the blue corner, the one on the right the red one
        _fighterLeft = AddEntity(CreateFighter(world, TintBlue));
        _fighterRight = AddEntity(CreateFighter(world, TintRed));
        _fighterRight.Flipped = true;

        spriteBatch.Add(_fighterLeft);
        spriteBatch.Add(_fighterRight);

        // Added after the fighters so whatever the duel throws up is drawn in front of them
        _dust = AddEntity(new ParticleRenderer2D(8192)
        {
            ParticleSize = 5,
            MaxAge = 0.7f,
            StartColor = new Vector3(0.55f, 0.5f, 0.45f),
            EndColor = new Vector3(0.08f, 0.07f, 0.06f),
            Gravity = new Vector2(0, 160)
        });
        _sparksBlue = AddEntity(CreateSparks(world, new Vector3(0.7f, 0.95f, 1.0f), new Vector3(0.0f, 0.2f, 0.9f)));
        _sparksRed = AddEntity(CreateSparks(world, new Vector3(1.0f, 0.95f, 0.7f), new Vector3(0.9f, 0.1f, 0.0f)));

        base.Initialize();
    }

    private static MenuFighter CreateFighter(PhysicsWorld world, Vector4 tint) => new(FIGHTER_SIZE, CHARACTER_DIRECTORY)
    {
        BaseTint = tint,
        Tint = tint,
        Body = world.CreateBody(PhysicsBodySimulationType.Dynamic)
    };

    /// <summary>
    /// Helper method to make the sparks of one corner. They are small, bouncy and land on the ground and on the fighters.
    /// </summary>
    private static ParticleRenderer2D CreateSparks(PhysicsWorld world, Vector3 startColor, Vector3 endColor)
    {
        var simulator = new PhysicsParticleSimulator2D(world)
        {
            Radius = 4.0f,
            Restitution = 0.45f,
            Friction = 4.0f,

            // The fighters wade through what lies about rather than firing it off
            BodyPush = 0.3f,
            BodyPushLimit = 260.0f
        };

        return new ParticleRenderer2D(16384, simulator)
        {
            ParticleSize = 4,
            MaxAge = 1.1f,
            StartColor = startColor,
            EndColor = endColor,
            Gravity = new Vector2(0, -900)
        };
    }

    public override void UpdateState(float dt)
    {
        AdvancePhase(dt);

        // How far through the current step we are, from 0 to 1
        float t = _phaseTime / PhaseDurations[(int)_phase];
        float gap = PlayPhase(t);

        float y = _center.Y + FIGHTER_SIZE / 2;
        _fighterLeft.MoveTo(new Vector2(_center.X - gap / 2, y), dt);
        _fighterRight.MoveTo(new Vector2(_center.X + gap / 2, y), dt);

        base.UpdateState(dt);
    }

    private void AdvancePhase(float dt)
    {
        _phaseTime += dt;

        float duration = PhaseDurations[(int)_phase];
        if (_phaseTime < duration) return;

        _phaseTime -= duration;
        _phase = (DuelPhase)(((int)_phase + 1) % PhaseDurations.Length);

        if (_phase == DuelPhase.Approach)
        {
            // They take turns, nobody likes a one sided fight
            _leftAttacks = !_leftAttacks;
            _struck = false;
        }
    }

    /// <summary>
    /// Helper method to pose both fighters for the step of the duel that is on.
    /// </summary>
    /// <returns>How far apart the two of them are standing.</returns>
    private float PlayPhase(float t)
    {
        MenuFighter attacker = _leftAttacks ? _fighterLeft : _fighterRight;
        MenuFighter defender = _leftAttacks ? _fighterRight : _fighterLeft;

        switch (_phase)
        {
            case DuelPhase.Approach:
                int stride = (int)(_phaseTime * 16) % 12;
                attacker.Show("run_loop", stride);
                defender.Show("run_loop", (stride + 6) % 12);

                KickUpDust(1);

                // Slow at both ends of the run
                return float.Lerp(GAP_FAR, GAP_NEAR, Ease.Apply(Easing.InOutSine, t));

            case DuelPhase.Strike:
                attacker.Show("kick", Math.Min(4, (int)(t * 6)));
                defender.Show("block", Math.Min(6, (int)(t * 9)));

                if (!_struck && t > STRIKE_MOMENT)
                {
                    _struck = true;
                    Strike(defender);
                }

                return GAP_NEAR;

            case DuelPhase.Recoil:
                attacker.Show("run_stop", Math.Min(4, (int)(t * 5)));
                defender.Show("run_stop", Math.Min(4, (int)(t * 5)));

                if (t < 0.6f) KickUpDust(2);

                // Fast at first, the way something slides to a halt
                return float.Lerp(GAP_NEAR, GAP_FAR, Ease.Apply(Easing.OutQuad, t));

            default:
                attacker.Show("idle", 0);
                defender.Show("idle", 0);
                return GAP_FAR;
        }
    }

    /// <summary>
    /// Helper method for the moment the kick lands on the block, with sparks in the colour of whoever threw it.
    /// </summary>
    private void Strike(MenuFighter defender)
    {
        var sparks = _leftAttacks ? _sparksBlue : _sparksRed;
        var impact = new Vector2(_center.X, _center.Y + FIGHTER_SIZE * 0.42f);

        // Most of it sprays past the one who blocked, the rest goes everywhere
        sparks.AddCone(impact, new Vector2(_leftAttacks ? 1 : -1, 0.5f), MathF.PI / 1.5f, 500, 620);
        sparks.AddBurst(impact, 250, 380);

        defender.Flash();
        Struck?.Invoke();
    }

    /// <summary>
    /// Helper method to throw dust out from under the feet of both fighters, away from the middle.
    /// </summary>
    private void KickUpDust(int count)
    {
        float feet = _center.Y + 14;

        _dust.AddCone(new Vector2(_fighterLeft.Transform.Position.X, feet), new Vector2(-1, 0.6f), MathF.PI / 3.0f, count, 140 + 120 * Random.Shared.NextSingle());
        _dust.AddCone(new Vector2(_fighterRight.Transform.Position.X, feet), new Vector2(1, 0.6f), MathF.PI / 3.0f, count, 140 + 120 * Random.Shared.NextSingle());
    }
}
