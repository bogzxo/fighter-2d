using System.Numerics;
using Bogz.Logging;
using Fighter2D.Character;
using Fighter2D.Logic;
using Fighter2D.Character.Controllers;
using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.Physics;
using Horizon.Rendering.Spriting;
using Riptide;

namespace Fighter2D.Scenes
{
    internal class NetworkingManager : IGameComponent
    {
        public required Player MasterPlayer { get; init; }
        public required Player SlavePlayer { get; init; }

        private Server _server;
        private Client _client;
        private bool _isServer;

        private IntervalRunnerFixedStep _intervalRunner;

        public NetworkingManager(string addr)
        {
            _client = new Client();
            if (!_client.Connect(addr, useMessageHandlers: false))
            {
                // TODO: fuck
                Console.WriteLine("Couldnt fucking connect to server.");
            }

            _client.MessageReceived += messageReceived;
        }

        private void messageReceived(object? sender, MessageReceivedEventArgs e)
        {
            ReadPlayerData(e.Message);
        }

        public NetworkingManager()
        {
            _server = new Server();
            _server.Start(7777, 1, useMessageHandlers:false);
            _server.MessageReceived += messageReceived;
            _isServer = true;
        }

        public void Initialize()
        {
            _intervalRunner = new IntervalRunnerFixedStep(1 / 60.0f, FixedUpdate);
        }

        private void FixedUpdate()
        {
            HandleNetworking();

            if (_isServer) _server.Update();
            else _client.Update();
        }

        private void HandleNetworking()
        {
            var msg = Message.Create(MessageSendMode.Reliable);

            msg = GeneratePlayerData(msg);

            if (_isServer) _server.SendToAll(msg);
            else _client.Send(msg);
        }

        private Message GeneratePlayerData(Message msg)
        {
            msg.AddFloat(MasterPlayer.Transform.Position.X);
            msg.AddFloat(MasterPlayer.Transform.Position.Y);

            return msg;
        }

        private void ReadPlayerData(Message msg)
        {
            SlavePlayer.Transform.Position = new(msg.GetFloat(), msg.GetFloat());
        }


        public void Render(float dt, object? obj = null)
        {

        }

        public void UpdateState(float dt)
        {

        }

        public void UpdatePhysics(float dt)
        {

        }

        public bool Enabled { get; set; }
        public string Name { get; set; } = "NetMan";
        public Entity Parent { get; set; }
    }

    internal class FightScene : Scene
    {
        public override Camera ActiveCamera { get; protected set; }

        private NetworkingManager networkingManager;

        // Sprite Rendering
        private SpriteBatch spriteBatch;

        private Sprite dummy;
        private PhysicsWorld world;
        //private UIRectangle

        internal static Player ControlledPlayer;
        internal static Player OtherPlayer;

        // General Rendering
        private Camera2D camera;


        internal readonly MoveList MoveList = new();

        // Map renderer
        private TileMap map;
        private MapLoader.MapDefinition mapDefinition;
        private readonly int gamepadIndex;

        public FightScene(MapLoader.MapDefinition mapDefinition, int gamepadIndex, Player? otherPlayer=null)
        {
            this.mapDefinition = mapDefinition;
            this.gamepadIndex = gamepadIndex;
            OtherPlayer = otherPlayer;
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
            spriteBatch.Add(AddEntity(ControlledPlayer = new Player()
            {
                Controller = new GamepadPlayerController(gamepadIndex),
                SpawnPosition = new(mapDefinition.SpawnPosition.X * map.TileSize.X, TileMapChunk.HEIGHT * map.TileSize.Y - mapDefinition.SpawnPosition.Y * map.TileSize.Y)
            }));

            if (OtherPlayer is not null)
            {
                spriteBatch.Add(AddEntity(OtherPlayer));
                if (OtherPlayer is NetworkPlayer netty)
                {
                    networkingManager = new NetworkingManager(netty.Address)
                    {
                        MasterPlayer = ControlledPlayer,
                        SlavePlayer = OtherPlayer
                    };
                }
            }
            else
            {
                OtherPlayer = new Player()
                {
                    Controller = new DummyPlayerController(),
                    SpawnPosition = new(mapDefinition.SpawnPosition.X * map.TileSize.X + 256, TileMapChunk.HEIGHT * map.TileSize.Y - mapDefinition.SpawnPosition.Y * map.TileSize.Y)
                };

                networkingManager = new NetworkingManager()
                {
                    MasterPlayer = ControlledPlayer,
                    SlavePlayer = OtherPlayer
                };
            }

            AddComponent(networkingManager);

            //spriteBatch.Add(AddEntity(dummy = new Player()
            //{
            //    Controller = new DummyPlayerController(MoveList),
            //    SpawnPosition = new(mapDefinition.SpawnPosition.X * map.TileSize.X + 196, TileMapChunk.HEIGHT * map.TileSize.Y - mapDefinition.SpawnPosition.Y * map.TileSize.Y)
            //}));

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
            //camera.Position = new Vector3(ControlledPlayer.Transform.Position, 0.0f);
            base.UpdateState(dt);
        }
    }
}