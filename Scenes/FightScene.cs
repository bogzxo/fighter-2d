using System;
using System.Numerics;
using Bogz.Logging;
using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.HUD;
using Fighter2D.Logic;
using Horizon.Engine;
using Horizon.Physics;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Scenes;

/// <summary>
/// Main arena scene where two absolute units crash out against each other.
/// </summary>
internal class FightScene : Scene
{
    public override Camera ActiveCamera { get; protected set; } = null!;

    private NetworkingManager? _networkingManager;
    private SpriteBatch _sceneSb = null!, _viewportSb = null!;
    private PhysicsWorld _world = null!;

    private VersusOverlay _overlay = null!;

    internal static Player ControlledPlayer = null!;
    internal static Player OtherPlayer = null!;

    private Camera2D _sceneCamera = null!, _viewportCamera = null!;
    internal readonly MoveList MoveList = new();
    private TileMap? _map;
    private readonly MapLoader.MapDefinition _mapDefinition;
    private readonly int _gamepadIndex;

    private Vector3 _exactCameraPosition;
    private readonly float _cameraFollowSpeed = 2.0f;

    public FightScene(MapLoader.MapDefinition mapDefinition, int gamepadIndex, Player? otherPlayer = null)
    {
        _mapDefinition = mapDefinition;
        _gamepadIndex = gamepadIndex;
        OtherPlayer = otherPlayer!;
    }

    public override void Initialize()
    {
        Engine.GL.ClearColor(System.Drawing.Color.Black);

        // Spin up physics world with heavy gravity so players wont float around like fuckin astronauts
        _world = AddComponent<PhysicsWorld>();
        _world.RenderDebug = false;
        _world.Gravity = new Vector2(0, -4000);

        LoadMap();

        Vector2 spawnPos = new(
            _mapDefinition.SpawnPosition.X * (_map?.TileSize.X ?? 16),
            TileMapChunk.HEIGHT * (_map?.TileSize.Y ?? 16) - _mapDefinition.SpawnPosition.Y * (_map?.TileSize.Y ?? 16)
        );

        ActiveCamera = _sceneCamera = AddEntity<Camera2D>(new(Engine.WindowManager.ViewportSize / 2.0f));
        _viewportCamera = AddEntity<Camera2D>(new(VersusOverlay.ViewportSize));

        _sceneSb = AddEntity<SpriteBatch>();

        // Spawn P1 (Master/Controlled Player)
        ControlledPlayer = new Player()
        {
            Controller = new GamepadPlayerController(_gamepadIndex),
            SpawnPosition = spawnPos
        };
        _sceneSb.Add(AddEntity(ControlledPlayer));

        _viewportSb = AddEntity<SpriteBatch>();
        _overlay = AddEntity(new VersusOverlay(_viewportSb));
        _viewportSb.CustomCamera = _viewportCamera;

        // Spawn P2 (Network Slave or Local Dummy)
        if (OtherPlayer is not null)
        {
            _sceneSb.Add(AddEntity(OtherPlayer));
            if (OtherPlayer is NetworkPlayer netty)
            {
                // We are client connecting to host
                _networkingManager = new NetworkingManager(netty.Address)
                {
                    MasterPlayer = ControlledPlayer,
                    SlavePlayer = OtherPlayer
                };
            }
            else
            {
                // Local P2
                _networkingManager = new NetworkingManager()
                {
                    MasterPlayer = ControlledPlayer,
                    SlavePlayer = OtherPlayer
                };
            }
        }
        else
        {
            // Host server mode with local dummy opponent
            OtherPlayer = new Player()
            {
                Controller = new DummyPlayerController(),
                SpawnPosition = new(spawnPos.X + 256, spawnPos.Y)
            };
            _sceneSb.Add(AddEntity(OtherPlayer));

            _networkingManager = new NetworkingManager()
            {
                MasterPlayer = ControlledPlayer,
                SlavePlayer = OtherPlayer
            };
        }

        if (_networkingManager != null)
        {
            AddComponent(_networkingManager);
        }

        _sceneCamera.Position = new Vector3(spawnPos.X, spawnPos.Y, 0.0f);
        _exactCameraPosition = _sceneCamera.Position;

        base.Initialize();
    }

    private void LoadMap()
    {
        if (!TileMap.TryFromTiledMap(this, "Assets/maps/" + _mapDefinition.FileName, out _map) || _map is null)
        {
            throw new Exception("Failed to load map file... fucked up Tiled XML or missing file.");
        }

        AddEntity(_map);

        _map.ParallaxIndex = 2;
        _map.ClippingOffset = 0.1f;

        // Build static collision boxes for map tiles
        PhysicsBodyComponent2D worldBody = _world.CreateBody(PhysicsBodySimulationType.Static);

        for (int x = 0; x < _map.Width * TileMapChunk.WIDTH; x++)
        {
            for (int y = 0; y < _map.Height * TileMapChunk.HEIGHT; y++)
            {
                for (int z = 0; z < _map.Depth; z++)
                {
                    Tile? tile = _map[x, y, z];
                    if (tile?.PhysicsData.IsCollidable == true)
                    {
                        worldBody.CreateRectangularFixture(tile.GlobalPosition - _map.TileSize / 2, _map.TileSize);
                    }
                }
            }
        }
    }

    public override void UpdateState(float dt)
    {
        // Smooth camera follow so the view doesn't violently snap to the pixel grid
        if (ControlledPlayer != null && Vector3.DistanceSquared(_sceneCamera.Position, new Vector3(ControlledPlayer.Transform.Position, _sceneCamera.Position.Z)) > 1000.0f)
        {
            Vector3 targetPosition = new Vector3(ControlledPlayer.Transform.Position, 0.0f);
            float smoothFactor = 1.0f - MathF.Exp(-_cameraFollowSpeed * dt);

            _exactCameraPosition = Vector3.Lerp(_exactCameraPosition, targetPosition, smoothFactor);

            _sceneCamera.Position = new Vector3(
                MathF.Round(_exactCameraPosition.X),
                MathF.Round(_exactCameraPosition.Y),
                _exactCameraPosition.Z
            );
        }

        base.UpdateState(dt);
    }

    protected override void DisposeOther()
    {
        // Shut down sockets cleanly so ports aren't left hanging open
        _networkingManager?.Dispose();
        base.DisposeOther();
    }
}