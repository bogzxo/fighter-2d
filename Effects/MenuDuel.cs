using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Fighter2D.Character;

using Horizon.Engine;
using Horizon.Physics;
using Horizon.Physics.Fixtures;
using Horizon.Physics.Simulation;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Effects;

/// <summary>
/// Two fighters going at each other forever, for the background of the menu.
/// Nobody plays them and nobody gets hurt (unfortunatly), the same sprite sheet the players use is ticked shown frame by frame.
/// They have a physics world of their own, with nothing in it but the ground and the two of them as the outlines of
/// whatever frame they are showing (see <see cref="CharacterBoxes"/>): that is what the sparks of a kick land on and bounce off.
/// </summary>
internal class MenuDuel : GameObject
{
    // Everything is in the units of the camera (pixels of the window)
    private const float FIGHTER_SIZE = 448;
    private const float GAP_FAR = 620;
    private const float GAP_NEAR = 170;

    private const string CHARACTER_DIRECTORY = "Assets/sprites/characters/the_man";

    // The ground the sparks land on: how far it reaches to either side of the middle, and how thick it is
    private const float GROUND_REACH = 4000;
    private const float GROUND_DEPTH = 200;

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
    /// A fighter of the duel, the same sprite sheet the players use but shown frame by frame.
    /// </summary>
    private sealed class MenuFighter() : Sprite(new Vector2(FIGHTER_SIZE))
    {
        public Vector4 BaseTint;

        // How much of the flash of a hit is left, 1 right after it
        public float Flash;

        // What the sparks run into of us: the outline of the frame we are showing, wherever the duel has put us
        public PhysicsBodyComponent2D Body = null!;
        private OutlinePhysicsFixture? outline;
        private CharacterBoxes? boxes;
        private (string? Animation, int Frame, bool Flipped) shown;
        private Vector2[] pieces = [];

        public override void Initialize()
        {
            base.Initialize();

            var (success, sheet, manager) = SpriteSheet.LoadSpriteSheetFromDirectory(CHARACTER_DIRECTORY);

            this.Spritesheet = sheet;
            this.AnimationManager = manager;

            if (success) boxes = CharacterBoxes.Load(CHARACTER_DIRECTORY, manager);

            // The duel decides which frame is shown
            AnimationManager.AnimateFrames = false;
            SetAnimation("idle");
        }

        public void Show(string animation, int frame)
        {
            // The sheet is loaded on the render thread, which can be after the first update
            if (AnimationManager is null) return;

            SetAnimation(animation);
            AnimationManager.SetFrame(animation, (uint)frame);

            // Made here rather than along with the sheet: this is the thread the physics runs on
            outline ??= Body.CreateOutlineFixture("outline");

            // The outline only needs handing over when what is drawn of us has changed
            if (shown == (animation, frame, Flipped)) return;
            shown = (animation, frame, Flipped);

            Vector2[] traced = boxes is not null && boxes.TryGet(animation, (uint)frame, out var found) ? found.Outline : [];
            if (pieces.Length < traced.Length) pieces = new Vector2[traced.Length];

            // At the size and the way round we are drawn
            Vector2 size = new(Flipped ? -FIGHTER_SIZE : FIGHTER_SIZE, FIGHTER_SIZE);
            for (int i = 0; i < traced.Length; i++) pieces[i] = traced[i] * size;

            outline.Set(pieces.AsSpan(0, traced.Length));
        }

        /// <summary>
        /// Helper method to put the fighter somewhere, body and all: how fast it got there is what it shoves the sparks aside with.
        /// </summary>
        public void MoveTo(Vector2 position, float dt)
        {
            Body.SetVelocity(dt > 0.0f ? (position - Body.Position) / dt : Vector2.Zero);
            Body.Position = position;
            Transform.Position = position;
        }
    }

    /// <summary>
    /// Called the moment a kick lands, for whoever wants to shake along.
    /// </summary>
    public event Action? Struck;

    // Where the fighters meet, and the height of the ground they stand on
    private readonly Vector2 center;

    private MenuFighter fighterLeft = null!, fighterRight = null!;
    private ParticleRenderer2D particlesDust = null!, sparksBlue = null!, sparksRed = null!;

    private DuelPhase duelPhase = DuelPhase.Rest;
    private float phaseTime = 0;
    private bool leftAttacks = false;
    private bool struck = false;

    /// <param name="center">Where the fighters meet (X) and the ground they stand on (Y)</param>
    public MenuDuel(Vector2 center)
    {
        this.center = center;
        Name = "Menu Duel";
    }

    public override void Initialize()
    {
        // A world with nothing pulling on the fighters (the duel puts them where they are), only the sparks fall
        var world = AddComponent<PhysicsWorld>();

        // The ground they stand on, from the middle a long way to either side
        var ground = world.CreateBody(PhysicsBodySimulationType.Static, new Vector2(center.X - GROUND_REACH, center.Y - GROUND_DEPTH));
        ground.CreateRectangularFixture(Vector2.Zero, new Vector2(GROUND_REACH * 2, GROUND_DEPTH));

        var spriteBatch = AddEntity<SpriteBatch>();

        // The fighter on the left is the blue corner, the one on the right the red one
        fighterLeft = AddEntity(new MenuFighter { BaseTint = TintBlue, Tint = TintBlue, Body = world.CreateBody(PhysicsBodySimulationType.Dynamic) });
        fighterRight = AddEntity(new MenuFighter { BaseTint = TintRed, Tint = TintRed, Body = world.CreateBody(PhysicsBodySimulationType.Dynamic) });
        fighterRight.Flipped = true;

        spriteBatch.Add(fighterLeft);
        spriteBatch.Add(fighterRight);

        // Whatever the duel throws up is drawn in front of the fighters
        particlesDust = AddEntity(new ParticleRenderer2D(8192)
        {
            ParticleSize = 5,
            MaxAge = 0.7f,
            StartColor = new Vector3(0.55f, 0.5f, 0.45f),
            EndColor = new Vector3(0.08f, 0.07f, 0.06f),
            Gravity = new Vector2(0, 160)
        });
        // The sparks land on the ground and on the fighters, to the pixel of what is drawn of them
        sparksBlue = AddEntity(new ParticleRenderer2D(16384, CreateSparkSimulator(world))
        {
            ParticleSize = 4,
            MaxAge = 1.1f,
            StartColor = new Vector3(0.7f, 0.95f, 1.0f),
            EndColor = new Vector3(0.0f, 0.2f, 0.9f),
            Gravity = new Vector2(0, -900)
        });
        sparksRed = AddEntity(new ParticleRenderer2D(16384, CreateSparkSimulator(world))
        {
            ParticleSize = 4,
            MaxAge = 1.1f,
            StartColor = new Vector3(1.0f, 0.95f, 0.7f),
            EndColor = new Vector3(0.9f, 0.1f, 0.0f),
            Gravity = new Vector2(0, -900)
        });

        base.Initialize();
    }

    /// <summary>
    /// Helper method to make what moves the sparks: small, bouncy and quick to come to rest.
    /// </summary>
    private static PhysicsParticleSimulator2D CreateSparkSimulator(PhysicsWorld world) => new(world)
    {
        Radius = 4.0f,
        Restitution = 0.45f,
        Friction = 4.0f,

        // The fighters wade through what lies about rather than firing it off
        BodyPush = 0.3f,
        BodyPushLimit = 260.0f
    };

    public override void UpdateState(float dt)
    {
        UpdateDuel(dt);
        base.UpdateState(dt);
    }

    private void UpdateDuel(float dt)
    {
        phaseTime += dt;

        float duration = PhaseDurations[(int)duelPhase];
        if (phaseTime >= duration)
        {
            phaseTime -= duration;
            duelPhase = (DuelPhase)(((int)duelPhase + 1) % PhaseDurations.Length);

            if (duelPhase == DuelPhase.Approach)
            {
                // They take turns, nobody likes a one sided fight
                leftAttacks = !leftAttacks;
                struck = false;
            }
        }

        // How far through the current step we are, from 0 to 1
        float t = phaseTime / PhaseDurations[(int)duelPhase];
        float gap = GAP_FAR;

        MenuFighter attacker = leftAttacks ? fighterLeft : fighterRight;
        MenuFighter defender = leftAttacks ? fighterRight : fighterLeft;

        switch (duelPhase)
        {
            case DuelPhase.Approach:
                // Slow at both ends of the run
                gap = float.Lerp(GAP_FAR, GAP_NEAR, t * t * (3 - 2 * t));

                int stride = (int)(phaseTime * 16) % 12;
                attacker.Show("run_loop", stride);
                defender.Show("run_loop", (stride + 6) % 12);

                KickUpDust(1);
                break;

            case DuelPhase.Strike:
                gap = GAP_NEAR;

                attacker.Show("kick", Math.Min(4, (int)(t * 6)));
                defender.Show("block", Math.Min(6, (int)(t * 9)));

                if (!struck && t > 0.35f)
                {
                    struck = true;
                    Strike(defender);
                }
                break;

            case DuelPhase.Recoil:
                // Fast at first, the way something slides to a halt
                gap = float.Lerp(GAP_NEAR, GAP_FAR, 1 - (1 - t) * (1 - t));

                attacker.Show("run_stop", Math.Min(4, (int)(t * 5)));
                defender.Show("run_stop", Math.Min(4, (int)(t * 5)));

                if (t < 0.6f) KickUpDust(2);
                break;

            default:
                attacker.Show("idle", 0);
                defender.Show("idle", 0);
                break;
        }

        float y = center.Y + FIGHTER_SIZE / 2;
        fighterLeft.MoveTo(new Vector2(center.X - gap / 2, y), dt);
        fighterRight.MoveTo(new Vector2(center.X + gap / 2, y), dt);

        FadeFlash(fighterLeft, dt);
        FadeFlash(fighterRight, dt);
    }

    /// <summary>
    /// Helper method for the moment the kick lands on the block, sparks in the colour of whoever threw it.
    /// </summary>
    private void Strike(MenuFighter defender)
    {
        var sparks = leftAttacks ? sparksBlue : sparksRed;
        var impact = new Vector2(center.X, center.Y + FIGHTER_SIZE * 0.42f);

        // Most of it sprays past the one who blocked, the rest goes everywhere
        sparks.AddCone(impact, new Vector2(leftAttacks ? 1 : -1, 0.5f), MathF.PI / 1.5f, 500, 620);
        sparks.AddBurst(impact, 250, 380);

        defender.Flash = 1.0f;
        Struck?.Invoke();
    }

    /// <summary>
    /// Helper method to throw dust out from under the feet of both fighters, away from the middle.
    /// </summary>
    private void KickUpDust(int count)
    {
        float feet = center.Y + 14;

        particlesDust.AddCone(new Vector2(fighterLeft.Transform.Position.X, feet), new Vector2(-1, 0.6f), MathF.PI / 3.0f, count, 140 + 120 * Random.Shared.NextSingle());
        particlesDust.AddCone(new Vector2(fighterRight.Transform.Position.X, feet), new Vector2(1, 0.6f), MathF.PI / 3.0f, count, 140 + 120 * Random.Shared.NextSingle());
    }

    private static void FadeFlash(MenuFighter fighter, float dt)
    {
        fighter.Flash = MathF.Max(0, fighter.Flash - dt * 4.0f);
        fighter.Tint = Vector4.Lerp(fighter.BaseTint, Vector4.One, fighter.Flash);
    }
}
