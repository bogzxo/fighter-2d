using System.Numerics;

using Bogz.Logging.Loggers;

using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Fixtures;

using Horizon.Physics;

using Horizon.Core.Components.Physics2D;
using Horizon.GameEntity.Components.Physics2D;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Player;

internal class Player : Sprite
{
    internal static Player Instance;
    private static readonly Vector2 SIZE = new(128);

    internal ControllablePlayerMoveManager MoveManager { get; private set; }
    internal ParticleRenderer2D Particles { get; private set; }

    private bool _controlled;

    private PhysicsWorld world;
    public PhysicsBodyComponent2D PhysicsBody { get; internal set; }

    public Player(bool controlled=true) : base(SIZE)
    {
        this._controlled = controlled;
        if (this._controlled)
            Instance = this;
    }

    public override void Initialize()
    {
        world = Parent.GetComponent<PhysicsWorld>();
        // Create player physics body and feet fixture
        PhysicsBody = AddComponent(world.CreateBody(PhysicsBodySimulationType.Dynamic));
        PhysicsBody.CreateCircleFixture(Vector2.UnitY * -48, 16.0f);

        if (!LoadSpriteSheetFromDirectory("Assets/sprites/player"))
        {
            ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, "Failed to load player sprite!");
        }

        SetAnimation("idle");
        AnimationManager.Enabled = true;


        if (_controlled)
            MoveManager = AddComponent<ControllablePlayerMoveManager>();

        Particles = AddEntity(new ParticleRenderer2D(32768) { EndColor = new Vector3(0, 0, 0.6f) });

        base.Initialize();
    }
}