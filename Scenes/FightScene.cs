using System.Numerics;

using Fighter2D.Logic;
using Fighter2D.Player.Controllers;

using Horizon.Engine;
using Horizon.Physics;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Scenes
{
    internal class FightScene : Scene
    {
        public override Camera ActiveCamera { get; protected set; }

        // Sprite Rendering
        private SpriteBatch spriteBatch;

        private Sprite dummy;
        private PhysicsWorld world;
        //private UIRectangle

        internal static Player.Player ControlledPlayer;

        // General Rendering
        private Camera2D camera;


        internal readonly MoveList MoveList = new();

        // Map renderer
        private TileMap map;
        private MapLoader.MapDefinition mapDefinition;
        private readonly int gamepadIndex;

        public FightScene(MapLoader.MapDefinition mapDefinition, int gamepadIndex)
        {
            this.mapDefinition = mapDefinition;
            this.gamepadIndex = gamepadIndex;
        }

        public override void Initialize()
        {
            Engine.GL.ClearColor(System.Drawing.Color.Black);

            world = AddComponent<PhysicsWorld>();
            world.RenderDebug = false;

            world.Gravity = new Vector2(0, -4000);
            LoadMap();

            ActiveCamera = camera = AddEntity<Camera2D>(new(Engine.WindowManager.ViewportSize / 2.0f));

            spriteBatch = AddEntity<SpriteBatch>();
            spriteBatch.Add(AddEntity(ControlledPlayer = new Player.Player()
            {
                Controller = new GamepadPlayerController(gamepadIndex, MoveList),
                SpawnPosition = new(mapDefinition.SpawnPosition.X * map.TileSize.X, TileMapChunk.HEIGHT * map.TileSize.Y - mapDefinition.SpawnPosition.Y * map.TileSize.Y)
            }));


            spriteBatch.Add(AddEntity(dummy = new Player.Player()
            {
                Controller = new DummyPlayerController(MoveList),
                SpawnPosition = new(mapDefinition.SpawnPosition.X * map.TileSize.X + 196, TileMapChunk.HEIGHT * map.TileSize.Y - mapDefinition.SpawnPosition.Y * map.TileSize.Y)
            }));

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

            PhysicsBodyComponent2D worldBody = world.CreateBody(PhysicsBodySimulationType.Static);

            for (int x = 0; x < map.Width * TileMapChunk.WIDTH; x++)
            {
                for (int y = 0; y < map.Height * TileMapChunk.HEIGHT; y++)
                {
                    for (int z = 0; z < map.Depth; z++)
                    {
                        Tile? tile = map[x, y, z];
                        if (tile?.PhysicsData.IsCollidable == true)
                        {
                            worldBody.CreateRectangularFixture(tile.GlobalPosition - map.TileSize / 2, map.TileSize);
                        }
                    }
                }
            }
        }

        private Vector3 _exactCameraPosition;
        private float _cameraFollowSpeed = 2.0f; // Tweak this for looser/tighter follow

        public override void UpdateState(float dt)
        {
            if (Vector3.DistanceSquared(camera.Position, new Vector3(ControlledPlayer.Transform.Position, camera.Position.Z)) > 1000.0f)
            {
                Vector3 targetPosition = new Vector3(ControlledPlayer.Transform.Position, 0.0f);

                float smoothFactor = 1.0f - MathF.Exp(-_cameraFollowSpeed * dt);

                _exactCameraPosition = Vector3.Lerp(_exactCameraPosition, targetPosition, smoothFactor);

                camera.Position = new Vector3(
                    MathF.Round(_exactCameraPosition.X),
                    MathF.Round(_exactCameraPosition.Y),
                    _exactCameraPosition.Z // Usually keep Z as-is depending on your depth buffer
                );
            }
            //camera.Position = new Vector3(player.Transform.Position, 0.0f);
            base.UpdateState(dt);
        }
    }
}