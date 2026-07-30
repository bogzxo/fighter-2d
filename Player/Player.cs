using System.Numerics;

using Bogz.Logging.Loggers;

using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Fixtures;

using Horizon.Core.Components.Physics2D;
using Horizon.GameEntity.Components.Physics2D;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

namespace CumInstinctDuel.Player;

internal class Player : Sprite
{
    internal static Player Instance;
    private static readonly Vector2 SIZE = new(128);

    internal PlayerMoveManager MoveManager { get; private set; }
    public Box2DBodyComponent Box2DBody { get; private set; }
    public Body PhysicsBody => Box2DBody.Body;
    public Body PlayerBody { get; }
    internal ParticleRenderer2D Particles { get; private set; }

    public Player(in Body playerBody) : base(SIZE)
    {
        Instance = this;
        PlayerBody = playerBody;
    }

    public override void Initialize()
    {
        if (!LoadSpriteSheetFromDirectory("Assets/sprites/player"))
        {
            ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, "Failed to load player sprite!");
        }

        Parent.GetComponent<Box2DWorldComponent>().Enabled = false;

        SetAnimation("idle");
        AnimationManager.Enabled = true;

        Box2DBody = AddComponent(new Box2DBodyComponent(PlayerBody));
        MoveManager = AddComponent<PlayerMoveManager>();

        Particles = AddEntity(new ParticleRenderer2D(32768) { EndColor = new Vector3(0, 0, 0.6f) });

        SetupPhysicsFixtures();

        Parent.GetComponent<Box2DWorldComponent>().Enabled = true;
        base.Initialize();
    }

    private void SetupPhysicsFixtures()
    {
        PolygonShape torso = new();
        torso.SetAsBox(16, 16, new Vector2(0, -16), 0);

        CircleShape feet = new() { Radius = 16.0f, Center = new Vector2(0, -SIZE.Y / 2 + 16) };

        PolygonShape footShape = new();
        footShape.SetAsBox(SIZE.X * 0.1f, 2.0f, new Vector2(0, -SIZE.Y / 2f), 0);

        FixtureDef footFixtureDef = new()
        {
            shape = footShape,
            isSensor = true,
            userData = "FootSensor"
        };

        PhysicsBody.CreateFixture(footFixtureDef);
        PhysicsBody.CreateFixture(torso, 0.001f);
        PhysicsBody.CreateFixture(feet, 0.001f);

        PhysicsBody.SetFixedRotation(true);
        PhysicsBody.SetLinearDampling(5.0f);
        PhysicsBody.SetGravityScale(1.0f);
    }
}