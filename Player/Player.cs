using System.Numerics;

using Bogz.Logging.Loggers;

using Horizon.Physics;

using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Player;

internal class Player : Sprite
{
    private static readonly Vector2 SIZE = new(128);

    internal ControllablePlayerMoveManager MoveManager { get; private set; }
    internal ParticleRenderer2D Particles { get; private set; }
    public Vector2 SpawnPosition { get; init; }

    private bool _controlled;

    private PhysicsWorld world;
    public PhysicsBodyComponent2D PhysicsBody { get; internal set; }

    public Player(bool controlled = true) : base(SIZE)
    {
        this._controlled = controlled;
    }

    public override void Initialize()
    {
        world = Parent.GetComponent<PhysicsWorld>();
        // Create player physics body and feet fixture
        PhysicsBody = AddComponent(world.CreateBody(PhysicsBodySimulationType.Dynamic, SpawnPosition));
        PhysicsBody.CreateCircleFixture(Vector2.UnitY * -48, 16.0f);
        PhysicsBody.CreateCircleFixture(Vector2.UnitY * -64, 8.0f, true, "feet");

        PhysicsBody.LinearDrag = 16.0f;
        PhysicsBody.Mass = 1.0f;
        PhysicsBody.Restitution = 0.3f;

        if (!LoadSpriteSheetFromDirectory("Assets/sprites/player"))
        {
            ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, "Failed to load player sprite!");
        }

        SetAnimation("idle");
        AnimationManager.Enabled = true;


        if (_controlled)
            MoveManager = AddComponent(new ControllablePlayerMoveManager(this, 0));

        Particles = AddEntity(new ParticleRenderer2D(32768) { EndColor = new Vector3(0, 0, 0.6f) });

        base.Initialize();
    }
}