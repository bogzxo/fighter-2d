global using static Horizon.Rendering.Tiling<Fighter2D.Map.MapTileTexID>;

using System.Numerics;

using Box2D.NetStandard.Common;
using Box2D.NetStandard.Dynamics.World;
using Box2D.NetStandard.Dynamics.World.Callbacks;

using Fighter2D.Physics;
using Fighter2D.Player;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.GameEntity.Components.Physics2D;
using Horizon.OpenGL.Descriptions;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UI;

namespace Fighter2D;

internal class Program : Scene
{
    public override Camera ActiveCamera { get; protected set; }

    // Sprite Rendering
    private SpriteBatch spriteBatch;
    private Sprite player, dummy;
    private PhysicsWorld world;
    //private UIRectangle

    // General Rendering
    private Camera2D camera;

    // Map renderer
    private TileMap map;
    private MapLoader.MapDefinition mapDefinition;

    public Program(MapLoader.MapDefinition mapDefinition)
    {
        this.mapDefinition = mapDefinition;
    }

    public override void Initialize()
    {
        Engine.GL.ClearColor(System.Drawing.Color.Black);

        world = AddComponent<PhysicsWorld>();
        LoadMap();
        ActiveCamera = camera = AddEntity<Camera2D>(new(Engine.WindowManager.ViewportSize / 2.0f));

        spriteBatch = AddEntity<SpriteBatch>();

        spriteBatch.Add(player = AddEntity(new Player.Player()));

        spriteBatch.Add(dummy = AddEntity(new Player.Player(false)));

        camera.Position = new Vector3(mapDefinition.SpawnPosition.X * map.TileSize.X, TileMapChunk.HEIGHT * map.TileSize.Y - mapDefinition.SpawnPosition.Y * map.TileSize.Y, 0.0f);

        base.Initialize();
    }

    private void LoadMap()
    {
        if (!TileMap.TryFromTiledMap(this, "Assets/maps/" + mapDefinition.FileName, out map))
        {
            throw new Exception("Something bad happened...");
        }

        AddEntity(map);
        
        map.ParallaxIndex = 2;
        map.ClippingOffset = 0.1f;


        List<RectanglePhysicsFixture> tileFixtures = [];

        for (int x = 0; x < map.Width * TileMapChunk.WIDTH; x++)
        {
            for (int y = 0; y < map.Height * TileMapChunk.HEIGHT; y++)
            {
                for (int z = 0; z < map.Depth; z++)
                {
                    Tile? tile = map[x, y, z];
                    if (tile?.PhysicsData.IsCollidable == true)
                    {
                        tileFixtures.Add(new RectanglePhysicsFixture(tile.GlobalPosition - map.TileSize / 2, map.TileSize));
                    }
                }
            }
        }

        PhysicsBody worldBody = new() { 
            Fixtures = tileFixtures.ToArray(),
            SimulationType = PhysicsBodySimulationType.Static
        };
        world.AddBody(worldBody);
    }

    private Vector3 _exactCameraPosition;
    private float _cameraFollowSpeed = 2.0f; // Tweak this for looser/tighter follow
    public override void UpdateState(float dt)
    {
        if (Vector3.DistanceSquared(camera.Position, new Vector3(player.Transform.Position, camera.Position.Z)) > 1000.0f)
        {
            Vector3 targetPosition = new Vector3(player.Transform.Position, 0.0f);

            float smoothFactor = 1.0f - MathF.Exp(-_cameraFollowSpeed * dt);

            _exactCameraPosition = Vector3.Lerp(_exactCameraPosition, targetPosition, smoothFactor);

            camera.Position = new Vector3(
                MathF.Round(_exactCameraPosition.X),
                MathF.Round(_exactCameraPosition.Y),
                _exactCameraPosition.Z // Usually keep Z as-is depending on your depth buffer
            );
        }
        base.UpdateState(dt);
    }

    static void Main(string[] args)
    {
        var eng = new GameEngine(new GameEngineConfiguration
        {
            InitialScene = typeof(MainMenuScene),
            WindowConfiguration = WindowManagerConfiguration.Default1600x900 with
            {
                WindowTitle = Constants.WINDOW_TITLE,
            }
        });

        eng.SceneManager.ChangeInstance<MainMenuScene>();

        eng.Run();
    }
}
