global using static Horizon.Rendering.Tiling<CumInstinctDuel.Map.MapTileTexID>;

using System.Numerics;

using Box2D.NetStandard.Common;
using Box2D.NetStandard.Dynamics.World;
using Box2D.NetStandard.Dynamics.World.Callbacks;

using CumInstinctDuel.Player;
using CumInstinctDuel.Scenes;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.GameEntity.Components.Physics2D;
using Horizon.OpenGL.Descriptions;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UI;

namespace CumInstinctDuel;


internal class Program : Scene
{
    public override Camera ActiveCamera { get; protected set; }

    // Sprite Rendering
    private SpriteBatch spriteBatch;
    private Sprite player;
    private World world;
    private Box2DDebugRendererComponent debugRendererComponent;
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
        //debugRendererComponent = AddComponent<Box2DDebugRendererComponent>();
        //debugRendererComponent.AppendFlags(DrawFlags.Shape | DrawFlags.Joint | DrawFlags.CenterOfMass | DrawFlags.Aabb | DrawFlags.Pair);
        LoadMap();
        Engine.GL.ClearColor(System.Drawing.Color.Black);

        ActiveCamera = camera = AddEntity<Camera2D>(new(Engine.WindowManager.ViewportSize / 2.0f));

        spriteBatch = AddEntity<SpriteBatch>();

        spriteBatch.Add(player = AddEntity(new Player.Player(world.CreateBody(new Box2D.NetStandard.Dynamics.Bodies.BodyDef {
            type = Box2D.NetStandard.Dynamics.Bodies.BodyType.Dynamic,
            allowSleep = false,
            position = new Vector2(mapDefinition.SpawnPosition.X * map.TileSize.X, TileMapChunk.HEIGHT * map.TileSize.Y - mapDefinition.SpawnPosition.Y * map.TileSize.Y)
        }))));

        camera.Position = new Vector3(mapDefinition.SpawnPosition.X * map.TileSize.X, TileMapChunk.HEIGHT * map.TileSize.Y - mapDefinition.SpawnPosition.Y * map.TileSize.Y, 0.0f);

        base.Initialize();
    }

    private void LoadMap()
    {
        world = AddComponent(new Box2DWorldComponent(new Vector2(0, -2000f)));
        world.SetDebugDraw(debugRendererComponent);


        if (!TileMap.TryFromTiledMap(this, "Assets/maps/" + mapDefinition.FileName, out map))
        {
            throw new Exception("Something bad happened...");
        }

        AddEntity(map);
        
        map.ParallaxIndex = 2;
        map.ClippingOffset = 0.1f;

        for (int x = 0; x < map.Width * TileMapChunk.WIDTH; x++)
        {
            for (int y = 0; y < map.Height * TileMapChunk.HEIGHT; y++)
            {
                for (int z = 0; z < map.Depth; z++)
                {
                    Tile? tile = map[x, y, z];
                    if (tile?.PhysicsData.IsCollidable == true)
                    {
                        tile.TryGenerateCollider();
                    }
                }
            }
        }
    }


    public override void Render(float dt, object? obj = null)
    {
        base.Render(dt, obj);
        if (debugRendererComponent is not null)
        {
            debugRendererComponent.ClearBuffers();
            world.DrawDebugData();
        }
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
                WindowTitle = "Cum Instinct: Duel",
            }
        });

        eng.SceneManager.ChangeInstance<MainMenuScene>();

        eng.Run();
    }
}
