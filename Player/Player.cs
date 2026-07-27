using System;
using System.Numerics;

using Bogz.Logging.Loggers;

using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;

using Horizon.Core.Components.Physics2D;
using Horizon.Core.Primitives;
using Horizon.GameEntity.Components.Physics2D;
using Horizon.Input.Components;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

using ImGuiNET;

using Silk.NET.Input;

namespace CumInstinctDuel.Player;

internal class Player : Sprite
{
    internal static Player Instance;
    private static readonly Vector2 SIZE = new(128);

    internal PlayerMoveManager moveManager;

    public Box2DBodyComponent Box2DBody { get; private set; }
    public Body PhysicsBody { get => Box2DBody.Body; }
    public Body PlayerBody { get; }

    internal ParticleRenderer2D Particles;

    public Player(in Body playerBody)
        : base(SIZE)
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
        moveManager = AddComponent<PlayerMoveManager>();
        Box2DBody = AddComponent(new Box2DBodyComponent(PlayerBody));
        Particles = AddEntity(new ParticleRenderer2D(32768));
        Particles.EndColor = new Vector3(0, 0, 0.6f);

        PolygonShape torso = new();
        torso.SetAsBox(16,16,new Vector2(0, -16), 0);
        CircleShape feet = new() { Radius = 16.0f, Center = new Vector2(0, -SIZE.Y / 2 + 16) };

        var footShape = new Box2D.NetStandard.Collision.Shapes.PolygonShape();
        footShape.SetAsBox(SIZE.X * 0.1f, 2.0f, new Vector2(0, -SIZE.Y/ 2f), 0);

        var footFixtureDef = new Box2D.NetStandard.Dynamics.Fixtures.FixtureDef
        {
            shape = footShape,
            isSensor = true, // means it detects touch but doesn't bump into things
            userData = "FootSensor" // Tag it 
        };

        PhysicsBody.CreateFixture(footFixtureDef);


        PhysicsBody.CreateFixture(torso, 0.001f);
        PhysicsBody.CreateFixture(feet, 0.001f);
        PhysicsBody.SetFixedRotation(true);
        
        PhysicsBody.SetLinearDampling(2.5f);
        PhysicsBody.SetGravityScale(1.0f);


        Parent.GetComponent<Box2DWorldComponent>().Enabled = true;
        base.Initialize();
    }
}
