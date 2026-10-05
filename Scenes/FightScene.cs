using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

using Bogz.Logging;
using Bogz.Logging.Loggers;

using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.Effects;
using Fighter2D.HUD;
using Fighter2D.Logic;

using Horizon.Engine;
using Horizon.Physics;
using Horizon.Rendering;
using Horizon.Rendering.Lighting;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Particles.Simulation;
using Horizon.Rendering.Spriting;

using Silk.NET.OpenGL;

using TiledSharp;

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
    private HUDManager _hudManager;

    internal static Player ControlledPlayer = null!;
    internal static Player OtherPlayer = null!;
    internal static FightEffects Effects = null!;

    private Camera2D _sceneCamera = null!, _viewportCamera = null!;

    // Everything of the world is drawn through this, which is what lights it
    private DeferredRenderer2D _renderer = null!;
    private OcclusionMap2D? _occlusion;

    // A little light of their own that goes wherever the players go, so they can be made out wherever the map is dark
    private readonly Light2D[] _playerLights = new Light2D[2];
    private static readonly Vector2 PLAYER_LIGHT_OFFSET = new(0, -28);

    // How big the lights of a map are unless they say so themselves (light_size), enough for the edges of their shadows to be soft
    private const float MAP_LIGHT_SIZE = 5.0f;

    // How far in front of the map its lights hover unless they say so themselves (light_height). This is what the normal
    // and specular maps of the tiles go by: the lower a light the more it rakes across them
    private const float MAP_LIGHT_HEIGHT = 40.0f;
    internal readonly MoveList MoveList = new();
    private TileMap? _map;
    private readonly MapLoader.MapDefinition _mapDefinition;
    private readonly int _gamepadIndex;
    private Vector3 _exactCameraPosition;
    private readonly float _cameraFollowSpeed = 2.0f;

    // The emitters the map asked for, in the coordinates of Tiled
    private readonly List<(string Kind, Vector2 Position, float Rate)> _emitters = [];

    // The lights the map asked for, where they are is in the coordinates of Tiled until they are handed over
    private readonly List<Light2D> _mapLights = [];

    // The corners of the box around every tile of the map, the camera is kept inside of it
    private Vector2 _mapMin = new(float.MaxValue), _mapMax = new(float.MinValue);

    public FightScene(MapLoader.MapDefinition mapDefinition, int gamepadIndex, Player? otherPlayer = null)
    {
        _mapDefinition = mapDefinition;
        _gamepadIndex = gamepadIndex;
        OtherPlayer = otherPlayer!;
    }

    public override void Initialize()
    {
        // Spin up physics world with heavy gravity so players wont float around like fuckin astronauts
        _world = AddComponent<PhysicsWorld>();
        //_world.RenderDebug = true;
        _world.Gravity = new Vector2(0, -4000);

        // The cameras go first, so they are up to date by the time anything is drawn with them
        ActiveCamera = _sceneCamera = AddEntity<Camera2D>(new(Engine.WindowManager.ViewportSize / 2.0f));
        _viewportCamera = AddEntity<Camera2D>(new(Engine.WindowManager.ViewportSize));

        // Whatever is added to the renderer is part of the world and gets lit. The HUD stays with us instead,
        // which has it drawn over the finished picture as it is.
        _renderer = AddEntity(new DeferredRenderer2D((uint)Engine.WindowManager.ViewportSize.X, (uint)Engine.WindowManager.ViewportSize.Y)
        {
            Ambient = _mapDefinition.Lighting.Ambient,

            // The art is a unit of the world per pixel, drawn at twice that: the light follows the art
            LightingPixelSize = 1.0f
        });

        LoadMap();

        Vector2 spawnPos = new(
            _mapDefinition.SpawnPosition.X * (_map?.TileSize.X ?? 16),
            TileMapChunk.HEIGHT * (_map?.TileSize.Y ?? 16) - _mapDefinition.SpawnPosition.Y * (_map?.TileSize.Y ?? 16)
        );

        _sceneSb = _renderer.AddEntity<SpriteBatch>();

        // Spawn P1 (Master/Controlled Player)
        ControlledPlayer = new Player()
        {
            Controller = new LocalPlayerController(new GamepadPlayerInput(_gamepadIndex)),
            SpawnPosition = spawnPos
        };
        _sceneSb.Add(AddEntity(ControlledPlayer));

        // Sparks, dust and whatever drifts through the air of this map
        Effects = _renderer.AddEntity(new FightEffects(_sceneCamera, _mapDefinition.Ambience, _world, _renderer));
        SpawnEmitters();
        SpawnLights();

        _viewportSb = AddEntity<SpriteBatch>();
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
                Controller = new LocalPlayerController(new DummyPlayerInput()),
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

        _sceneCamera.Position = ClampToMap(new Vector3(spawnPos.X, spawnPos.Y, 0.0f));
        _exactCameraPosition = _sceneCamera.Position;

        _hudManager = AddComponent<HUDManager>();

        base.Initialize();
    }

    private void LoadMap()
    {
        if (!TileMap.TryFromTiledMap(this, "Assets/maps/" + _mapDefinition.FileName, ObjectSpawnerCallback, out _map) || _map is null)
        {
            throw new Exception("Failed to load map file... fucked up Tiled XML or missing file.");
        }

        _renderer.AddEntity(_map);

        _map.ParallaxIndex = 2;
        _map.ClippingOffset = 0.1f;

        // Build static collision boxes for map tiles
        PhysicsBodyComponent2D worldBody = _world.CreateBody(PhysicsBodySimulationType.Static);

        // What a player can't get through light can't either: the same tiles are what casts the shadows
        _renderer.Occlusion = _occlusion = new OcclusionMap2D(
            _map.Width * TileMapChunk.WIDTH, _map.Height * TileMapChunk.HEIGHT, -_map.TileSize / 2, _map.TileSize);

        for (int x = 0; x < _map.Width * TileMapChunk.WIDTH; x++)
        {
            for (int y = 0; y < _map.Height * TileMapChunk.HEIGHT; y++)
            {
                for (int z = 0; z < _map.Depth; z++)
                {
                    Tile? tile = _map[x, y, z];
                    if (tile is null) continue;

                    // Every tile counts towards the size of the map, solid or not
                    _mapMin = Vector2.Min(_mapMin, tile.GlobalPosition - _map.TileSize / 2);
                    _mapMax = Vector2.Max(_mapMax, tile.GlobalPosition + _map.TileSize / 2);

                    if (tile.PhysicsData.IsCollidable == true)
                    {
                        worldBody.CreateRectangularFixture(tile.GlobalPosition - _map.TileSize / 2, _map.TileSize);
                        _occlusion.Set(tile.GlobalPosition, true);
                    }
                }
            }
        }
    }

    private void ObjectSpawnerCallback(TmxObject? obj)
    {
        if (obj is null) return;

        if (obj.Properties.TryGetValue("emitter_type", out var kind) && obj.Properties.TryGetValue("emitter_rate", out var rateText))
        {
            if (!float.TryParse(rateText, NumberStyles.Float, CultureInfo.InvariantCulture, out float rate))
            {
                ConcurrentLogger.Instance.Log(LogLevel.Warning, $"Emitter '{obj.Name}' has a malformed emitter_rate '{rateText}'!");
                return;
            }

            // The map is loaded before there are any effects, they are handed over once both exist
            _emitters.Add((kind, new Vector2((float)obj.X, (float)obj.Y), rate));
        }

        // Anything with a radius to light up is a light, the rest of what there is to say about it is optional
        if (obj.Properties.TryGetValue("light_radius", out var radiusText))
        {
            if (!TryParseNumber(radiusText, out float radius) || radius <= 0.0f)
            {
                ConcurrentLogger.Instance.Log(LogLevel.Warning, $"Light '{obj.Name}' has a malformed light_radius '{radiusText}'!");
                return;
            }

            _mapLights.Add(new Light2D
            {
                Position = new Vector2((float)obj.X, (float)obj.Y),
                Radius = radius,
                Color = obj.Properties.TryGetValue("light_colour", out var colourText) ? ParseColour(colourText) : Vector3.One,
                Intensity = obj.Properties.TryGetValue("light_intensity", out var intensityText) && TryParseNumber(intensityText, out float intensity) ? intensity : 1.0f,
                Glow = obj.Properties.TryGetValue("light_glow", out var glowText) && TryParseNumber(glowText, out float glow) ? glow : 0.0f,
                Flicker = obj.Properties.TryGetValue("light_flicker", out var flickerText) && TryParseNumber(flickerText, out float flicker) ? flicker : 0.0f,
                Size = obj.Properties.TryGetValue("light_size", out var sizeText) && TryParseNumber(sizeText, out float size) ? size : MAP_LIGHT_SIZE,
                Height = obj.Properties.TryGetValue("light_height", out var heightText) && TryParseNumber(heightText, out float height) ? height : MAP_LIGHT_HEIGHT,
                CastsShadows = !obj.Properties.TryGetValue("light_shadows", out var shadowsText) || !bool.TryParse(shadowsText, out bool shadows) || shadows
            });
        }
    }

    private static bool TryParseNumber(string text, out float number) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    /// <summary>
    /// Helper method to read a colour the way Tiled writes them: #RRGGBB, or #AARRGGBB (the alpha is of no use to a light).
    /// Anything else is white.
    /// </summary>
    private static Vector3 ParseColour(string text)
    {
        ReadOnlySpan<char> digits = text.AsSpan().TrimStart('#');
        if (digits.Length == 8) digits = digits[2..];

        if (digits.Length != 6 || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
        {
            return Vector3.One;
        }

        return new Vector3((colour >> 16) & 0xFF, (colour >> 8) & 0xFF, colour & 0xFF) / 255f;
    }

    /// <summary>
    /// Helper method to work out where in the world something that was placed in Tiled is.
    /// </summary>
    private Vector2 TiledToWorld(Vector2 position)
    {
        if (_map is null) return position;

        // Tiled measures from the top left corner of the map with Y going down, and our tiles sit centred on their position
        float mapHeight = _map.Height * TileMapChunk.HEIGHT * _map.TileSize.Y;

        return new Vector2(position.X, mapHeight - position.Y) - _map.TileSize / 2;
    }

    /// <summary>
    /// Hands the emitters of the map over to the effects, which let their particles go from then on.
    /// </summary>
    private void SpawnEmitters()
    {
        if (_map is null) return;

        foreach (var (kind, position, rate) in _emitters)
        {
            if (!Effects.AddEmitter(kind, TiledToWorld(position), rate))
            {
                ConcurrentLogger.Instance.Log(LogLevel.Warning, $"Skipped a '{kind}' emitter, there is no such preset or its rate isn't above zero!");
            }
        }

        _emitters.Clear();
    }

    /// <summary>
    /// Hands the lights of the map over to the renderer, and gives each of the players one of their own.
    /// </summary>
    private void SpawnLights()
    {
        foreach (var light in _mapLights)
        {
            light.Position = TiledToWorld(light.Position);
            _renderer.AddLight(light);
        }

        _mapLights.Clear();

        // The darker the map the more the players need a light of their own, in broad daylight they need none
        Vector3 ambient = _mapDefinition.Lighting.Ambient;
        float brightness = (ambient.X + ambient.Y + ambient.Z) / 3.0f;
        float fill = Math.Clamp(0.9f - brightness, 0.0f, 0.5f);

        for (int i = 0; i < _playerLights.Length; i++)
        {
            // Only there to fill in: weak, wide and going through everything
            _playerLights[i] = _renderer.AddLight(new Light2D
            {
                Color = new Vector3(0.85f, 0.9f, 1.0f),
                Radius = 120.0f,
                Intensity = fill,
                CastsShadows = false
            });
        }
    }

    public override void UpdateState(float dt)
    {
        // Smooth camera follow so the view doesn't violently snap to the pixel grid
        if (ControlledPlayer != null && Vector3.DistanceSquared(_sceneCamera.Position, new Vector3(ControlledPlayer.Transform.Position, _sceneCamera.Position.Z)) > 1000.0f)
        {
            // Follow the player, but stop at the edges of the map so the clear colour is never seen
            Vector3 targetPosition = ClampToMap(new Vector3(ControlledPlayer.Transform.Position, 0.0f));
            float smoothFactor = 1.0f - MathF.Exp(-_cameraFollowSpeed * dt);

            _exactCameraPosition = Vector3.Lerp(_exactCameraPosition, targetPosition, smoothFactor);

            _sceneCamera.Position = new Vector3(
                MathF.Round(_exactCameraPosition.X),
                MathF.Round(_exactCameraPosition.Y),
                _exactCameraPosition.Z
            );
        }

        if (ControlledPlayer is not null && _playerLights[0] is not null)
        {
            _playerLights[0].Position = ControlledPlayer.Transform.Position + PLAYER_LIGHT_OFFSET;
        }

        if (OtherPlayer is not null && _playerLights[1] is not null)
        {
            _playerLights[1].Position = OtherPlayer.Transform.Position + PLAYER_LIGHT_OFFSET;
        }

        base.UpdateState(dt);
    }

    /// <summary>
    /// Helper method to keep what the camera sees inside of the map.
    /// </summary>
    private Vector3 ClampToMap(Vector3 position)
    {
        // A map without tiles has nothing to keep the camera in
        if (_mapMin.X > _mapMax.X) return position;

        Vector2 halfView = Engine.WindowManager.ViewportSize / 2.0f * _sceneCamera.Zoom / 2.0f;

        return new Vector3(
            ClampAxis(position.X, _mapMin.X, _mapMax.X, halfView.X),
            ClampAxis(position.Y, _mapMin.Y, _mapMax.Y, halfView.Y),
            position.Z
        );
    }

    private static float ClampAxis(float position, float min, float max, float halfView)
    {
        // A map smaller than the view can't fill it, the best we can do is centre it
        if (max - min <= halfView * 2) return (min + max) / 2;

        return Math.Clamp(position, min + halfView, max - halfView);
    }

    protected override void DisposeOther()
    {
        // Shut down sockets cleanly so ports aren't left hanging open
        _networkingManager?.Dispose();
        _occlusion?.Dispose();
        base.DisposeOther();
    }
}