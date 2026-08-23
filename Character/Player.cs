using System.Numerics;

using Bogz.Logging.Loggers;

using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Fixtures;

using Egui;
using Egui.Containers;
using Egui.Widgets;

using Fighter2D.Character.Controllers;
using Fighter2D.Logic;

using Horizon.Core.Components.Physics2D;
using Horizon.GameEntity.Components.Physics2D;
using Horizon.Physics;
using Horizon.Physics.Fixtures;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Character;

internal class Player() : Sprite(SIZE)
{
    private static readonly Vector2 SIZE = new(128);
    internal PlayerController Controller { get; init; }
    internal ParticleRenderer2D Particles { get; private set; }
    public Vector2 SpawnPosition { get; init; }

    public readonly MoveList MoveList = new();

    private PhysicsWorld world;
    public PhysicsBodyComponent2D PhysicsBody { get; internal set; }

    public CirclePhysicsFixture HitboxFixture { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        if (!LoadSpriteSheetFromDirectory("Assets/sprites/player"))
        {
            ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, "Failed to load player sprite!");
        }

        AnimationManager.AnimateFrames = false;

        world = Parent.GetComponent<PhysicsWorld>();
        // Create player physics body and feet fixture
        PhysicsBody = AddComponent(world.CreateBody(PhysicsBodySimulationType.Dynamic, SpawnPosition));

        PhysicsBody.CreateCircleFixture(Vector2.UnitY * -48, 16.0f);
        PhysicsBody.CreateCircleFixture(new (0, -32), 16.0f);
        PhysicsBody.CreateCircleFixture(Vector2.UnitY * -64, 12.0f, true, "feet");
        HitboxFixture = PhysicsBody.CreateCircleFixture(Vector2.UnitY * -24, 32, true, "hitbox");

        PhysicsBody.LinearDrag = 16.0f;
        PhysicsBody.Mass = 1.0f;
        PhysicsBody.Restitution = 0.3f;

        SetAnimation("idle");

        AddComponent(Controller);

        Particles = AddEntity(new ParticleRenderer2D(32768) { EndColor = new Vector3(0, 0, 0.6f) });
    }

    public override void RenderUi(Ui root)
    {
        new Window("Player")
            .Show(root.Ctx, ui =>
            {
                ui.Heading("Player Information");
                ui.Label($"Current Move: {Controller.CurrentMove.Id}");
                ui.Label($"Current Stance: {Controller.StateTracker.CurrentStance}");
                ui.Label($"Current Status: {Controller.StateTracker.CurrentStatus}");
                ui.Label($"Is Grounded: {Controller.StateTracker.IsGrounded}");
                ui.Label($"CanInterrupt: {Controller.CanInterrupt}");
            });

        base.RenderUi(root);
    }
}