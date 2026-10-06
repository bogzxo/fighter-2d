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
using Fighter2D.Match;
using Fighter2D.Networking;

using Horizon.Engine;
using Horizon.Physics;
using Horizon.Rendering;
using Horizon.Rendering.Lighting;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Particles.Simulation;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.Tiling;

using Silk.NET.OpenGL;

namespace Fighter2D.Scenes;

/// <summary>
/// Main arena scene where two absolute units crash out against each other.
/// </summary>
internal class FightScene : Scene
{
    public override Camera ActiveCamera { get; protected set; } = null!;

    private FightNetwork? _network;
    private SpriteBatch _sceneSb = null!, _viewportSb = null!;
    private PhysicsWorld _world = null!;
    private HUDManager _hudManager;

    internal static Player ControlledPlayer = null!;
    internal static Player OtherPlayer = null!;
    internal static FightEffects Effects = null!;

    // The networking of an online fight, null for a fight that stays on this machine
    internal static FightNetwork? Network;

    // What runs the rounds of the fight: when the players may move, who took which round and when the match is over
    internal static RoundDirector? Round;
    private RoundDirector _round = null!;

    /// <summary>
    /// What the match is played by, the defaults for a fight nobody set any rules for.
    /// </summary>
    public MatchRules Rules { get; init; } = new();

    /// <summary>
    /// The connection to the machine the other player is on, for an online fight.
    /// </summary>
    public Fighter2D.Networking.NetSession? Session { get; init; }

    /// <summary>
    /// How far to the right of where the map says our player starts, for when they have the other corner.
    /// </summary>
    public float SpawnOffset { get; init; }

    /// <summary>
    /// The character our player picked, null for the first one there is.
    /// </summary>
    public string? CharacterId { get; init; }

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
    private TileMap? _map;
    private MapLoader.MapDefinition _mapDefinition;
    private readonly int _gamepadIndex;
    private Vector3 _exactCameraPosition;

    // What is in the air of the map, and how far its thunder has the camera off of where it belongs right now
    private Weather _weather = null!;
    private Vector2 _cameraShake;

    // A pixel of the screen in units of the world (the art is drawn at twice its size), and how little the camera
    // has to move in an update to count as standing still
    private const float CAMERA_PIXEL = 0.5f;
    private const float CAMERA_STILL = 0.02f;
    private readonly float _cameraFollowSpeed = 2.0f;

    // The emitters the map asked for, kept until there are effects to hand them to
    private readonly List<(string Kind, Vector2 Position, float Rate)> _emitters = [];

    // The lights the map asked for
    private readonly List<Light2D> _mapLights = [];

    // Where the map says the players start (a spawn object with player set to 1 or 2), for the maps that say
    private readonly Vector2?[] _mapSpawns = new Vector2?[2];

    // How far apart the players start on a map that only says where the first one does
    private const float SPAWN_GAP = 256;

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
        // The rules have a say in what the map is like (its weather), before anything is made out of it
        _mapDefinition = Rules.Apply(_mapDefinition);

        // Spin up physics world with heavy gravity so players wont float around like fuckin astronauts
        _world = AddComponent<PhysicsWorld>();
        //_world.RenderDebug = true;
        _world.Gravity = new Vector2(0, -8000);

        // The cameras go first, so they are up to date by the time anything is drawn with them
        ActiveCamera = _sceneCamera = AddEntity<Camera2D>(new(Engine.WindowManager.ViewportSize / 2.0f));
        _viewportCamera = AddEntity<Camera2D>(new(Engine.WindowManager.ViewportSize));

        // Whatever is added to the renderer is part of the world and gets lit. The HUD stays with us instead,
        // which has it drawn over the finished picture as it is: over the glass, not behind it.
        _renderer = AddEntity(new DeferredRenderer2D((uint)Engine.WindowManager.ViewportSize.X, (uint)Engine.WindowManager.ViewportSize.Y)
        {
            Ambient = _mapDefinition.Lighting.Ambient,

            // The art is a unit of the world per pixel, drawn at twice that: the light follows the art
            LightingPixelSize = 1.0f
        });

        // The camera follows the players on whole pixels, which without this is seen as stepping
        Screen.Blur(_renderer);

        // The world is all there is behind the glass, so it is its own
        Screen.Glass(_renderer);

        LoadMap();

        // The map has the last word on where the players start, maps.hor is for the ones that don't say
        Vector2 leftSpawn = _mapSpawns[0] ?? new Vector2(
            _mapDefinition.SpawnPosition.X * (_map?.TileSize.X ?? 16),
            MapLoader.SPAWN_BASE_ROW * (_map?.TileSize.Y ?? 16) - _mapDefinition.SpawnPosition.Y * (_map?.TileSize.Y ?? 16)
        );
        Vector2 rightSpawn = _mapSpawns[1] ?? leftSpawn + new Vector2(SPAWN_GAP, 0);

        // Whoever has the other corner is told with an offset, which is all that is known before the map is
        bool hasRightCorner = SpawnOffset > 0;
        Vector2 spawnPos = hasRightCorner ? rightSpawn : leftSpawn;

        // The layers of the map that scroll at their own speed are where they were drawn when the fight starts
        if (_map is not null) _map.ParallaxOrigin = (leftSpawn + rightSpawn) / 2.0f;

        _sceneSb = _renderer.AddEntity<SpriteBatch>();

        // Spawn P1 (Master/Controlled Player)
        ControlledPlayer = new Player()
        {
            Controller = new LocalPlayerController(new GamepadPlayerInput(_gamepadIndex)),
            SpawnPosition = spawnPos,
            CharacterId = CharacterId
        };
        _sceneSb.Add(AddEntity(ControlledPlayer));

        // Blood and dust, and whatever the air of this map is up to
        Effects = _renderer.AddEntity(new FightEffects(_world, _renderer));
        _weather = _renderer.AddEntity(new Weather(_sceneCamera, _mapDefinition.Ambience, _mapDefinition.Storm, _world, _renderer));
        SpawnEmitters();
        SpawnLights();

        // Whatever of the map is marked as being in front goes over the players and the effects
        if (_map is not null) _renderer.AddEntity(_map.Foreground);

        _viewportSb = AddEntity<SpriteBatch>();
        _viewportSb.CustomCamera = _viewportCamera;


        // Spawn P2 (somebody on this machine, somebody on another one or the dummy)
        if (OtherPlayer is null)
        {
            OtherPlayer = new Player()
            {
                Controller = new LocalPlayerController(new DummyPlayerInput())
            };
        }

        // They get the corner we don't have
        OtherPlayer.SpawnPosition = hasRightCorner ? leftSpawn : rightSpawn;
        _sceneSb.Add(AddEntity(OtherPlayer));

        _sceneCamera.Position = ClampToMap(new Vector3(spawnPos.X, spawnPos.Y, 0.0f));
        _exactCameraPosition = _sceneCamera.Position;

        // The rounds are run from here on. Against another machine it is the host that runs them, the other one is told how they go
        Round = _round = AddComponent(new RoundDirector(Rules, ControlledPlayer, OtherPlayer)
        {
            IsAuthority = Session is null || Session.IsHost,
            Finished = LeaveFight,
            SkipRequested = ContinuePressed
        });

        // Only a fight against another machine has anything to say to it
        Network = null;
        if (Session is not null)
        {
            Network = _network = AddComponent(new FightNetwork(Session, _round)
            {
                LocalPlayer = ControlledPlayer,
                RemotePlayer = OtherPlayer
            });
        }

        // The HUD only shows what the rounds are up to and so comes after them

        _hudManager = AddComponent<HUDManager>();

        base.Initialize();
    }

    private void LoadMap()
    {
        string path = Fighter2D.Content.GameContent.PathOf(Fighter2D.Content.GameContent.MAPS_DIRECTORY + "/" + _mapDefinition.FileName);

        if (!TileMap.TryLoad(path, out _map) || _map is null)
        {
            throw new Exception("Failed to load map file... fucked up Tiled XML or missing file.");
        }

        // The middle of the bottom left tile is at zero, which is where it has always been: spawn_pos of maps.hor counts on it
        _map.Origin = -_map.TileSize / 2;

        _renderer.AddEntity(_map);

        // Everything that was placed in the map rather than painted, by what it is. The templates in Assets/maps/objects
        // are what put these together in Tiled, an object that has the same properties without one works just as well
        _map.DispatchObjects(objects => objects
            .OfClass("spawn", ReadSpawn)
            .WithProperty("emitter_type", ReadEmitter)
            .WithProperty("light_radius", ReadLight));

        // Build static collision boxes for the map, tiles next to each other come as one box
        PhysicsBodyComponent2D worldBody = _world.CreateBody(PhysicsBodySimulationType.Static);

        foreach (TileMapBox box in _map.BuildColliders())
        {
            worldBody.CreateRectangularFixture(box.Min, box.Size);
        }

        // What casts the shadows is up to the layers of the map (CastsShadows): the platforms do, whether or not
        // there is anything to stand on. A layer that doesn't say is taken at whether it is collidable.
        _renderer.Occlusion = _occlusion = new OcclusionMap2D(_map.Width, _map.Height, _map.Origin, _map.TileSize);

        foreach (Vector2 solid in _map.ShadowCasters())
        {
            _occlusion.Set(solid, true);
        }

        // Every tile counts towards the box the camera is kept in, solid or not
        foreach (TileMapCell cell in _map.Tiles())
        {
            _mapMin = Vector2.Min(_mapMin, cell.Centre - _map.TileSize / 2);
            _mapMax = Vector2.Max(_mapMax, cell.Centre + _map.TileSize / 2);
        }
    }

    /// <summary>
    /// Called for every spawn of the map: where a player starts, and which one (player: 1 or 2).
    /// </summary>
    private void ReadSpawn(TileMapObject spawn)
    {
        int player = spawn.Properties.GetInt("player", 1);
        if (player < 1 || player > _mapSpawns.Length)
        {
            ConcurrentLogger.Instance.Log(LogLevel.Warning, $"Spawn '{spawn.Name}' is for player {player}, there are only {_mapSpawns.Length}!");
            return;
        }

        _mapSpawns[player - 1] = spawn.Position;
    }

    /// <summary>
    /// Called for every object of the map that has an emitter_type: something that lets particles go.
    /// </summary>
    private void ReadEmitter(TileMapObject emitter)
    {
        if (!emitter.Properties.TryGetFloat("emitter_rate", out float rate))
        {
            ConcurrentLogger.Instance.Log(LogLevel.Warning, $"Emitter '{emitter.Name}' has no emitter_rate, or one that isn't a number!");
            return;
        }

        // The map is loaded before there are any effects, they are handed over once both exist
        _emitters.Add((emitter.Properties.GetString("emitter_type"), emitter.Position, rate));
    }

    /// <summary>
    /// Called for every object of the map that has a light_radius. Anything with a radius to light up is a light,
    /// the rest of what there is to say about it is optional.
    /// </summary>
    private void ReadLight(TileMapObject light)
    {
        float radius = light.Properties.GetFloat("light_radius");
        if (radius <= 0.0f)
        {
            ConcurrentLogger.Instance.Log(LogLevel.Warning, $"Light '{light.Name}' has a light_radius that isn't a number above zero!");
            return;
        }

        Vector4 colour = light.Properties.GetColor("light_colour", Vector4.One);

        _mapLights.Add(new Light2D
        {
            Position = light.Position,
            Radius = radius,
            Color = new Vector3(colour.X, colour.Y, colour.Z),
            Intensity = light.Properties.GetFloat("light_intensity", 1.0f),
            Glow = light.Properties.GetFloat("light_glow"),
            Flicker = light.Properties.GetFloat("light_flicker"),
            Size = light.Properties.GetFloat("light_size", MAP_LIGHT_SIZE),
            Height = light.Properties.GetFloat("light_height", MAP_LIGHT_HEIGHT),
            CastsShadows = light.Properties.GetBool("light_shadows", true)
        });
    }

    /// <summary>
    /// Hands the emitters of the map over to the effects, which let their particles go from then on.
    /// </summary>
    private void SpawnEmitters()
    {
        foreach (var (kind, position, rate) in _emitters)
        {
            if (!Effects.AddEmitter(kind, position, rate))
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
        // Thunder and the blows that land rattle the view about wherever it is, by as much as they have changed their mind since
        Vector2 shake = _weather.Shake + Effects.Shake;
        if (shake != _cameraShake)
        {
            _sceneCamera.Position += new Vector3(shake - _cameraShake, 0.0f);
            _cameraShake = shake;
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

    public override void UpdatePhysics(float dt)
    {
        base.UpdatePhysics(dt);
        FollowPlayers(dt);
    }

    /// <summary>
    /// Called by the rounds once the match is over and its result has been up for long enough.
    /// </summary>
    private void LeaveFight()
    {
        Engine.SetScene(new MainMenuScene());
    }

    /// <summary>
    /// Helper method to test whether anybody has pressed the button that moves on from the result of the match.
    /// </summary>
    private static bool ContinuePressed()
    {
        foreach (Horizon.Input2.Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            if (gamepad.WasPressed(Horizon.Input2.GamepadInput.A) || gamepad.WasPressed(Horizon.Input2.GamepadInput.Start)) return true;
        }

        return false;
    }

    /// <summary>
    /// Helper method to move the camera along with the players, once for every step of the physics.
    /// </summary>
    private void FollowPlayers(float dt)
    {
        if (_sceneCamera is null) return;

        // The players are drawn where the physics has just put them, and the camera is put where it goes by that
        // in the same breath: here, right after the step, and not in the update of the state. The physics has a
        // loop of its own, and the state can come round twice between two of its steps or not at all. A camera
        // that followed from there saw the player stand still one moment and go twice as far the next, and with
        // it the player flickered between two places on the screen as fast as the updates came.
        SyncToPhysics(ControlledPlayer);
        SyncToPhysics(OtherPlayer);

        // Smooth camera follow so the view doesn't violently snap to the pixel grid
        if (ControlledPlayer != null && Vector3.DistanceSquared(_sceneCamera.Position, new Vector3(ControlledPlayer.Transform.Position, _sceneCamera.Position.Z)) > 1000.0f)
        {
            // Follow the player, but stop at the edges of the map so the clear colour is never seen
            Vector2 player = ControlledPlayer.Transform.Position;
            Vector3 targetPosition = ClampToMap(new Vector3(player, 0.0f));
            float smoothFactor = 1.0f - MathF.Exp(-_cameraFollowSpeed * dt);

            Vector3 before = _exactCameraPosition;
            _exactCameraPosition = Vector3.Lerp(_exactCameraPosition, targetPosition, smoothFactor);

            if (Vector3.DistanceSquared(before, _exactCameraPosition) > CAMERA_STILL * CAMERA_STILL)
            {
                // On the move: a whole number of pixels of the screen away from the player, rather than on whole
                // pixels of its own. Rounded by itself the camera steps at other moments than the player does,
                // and the player wobbles by a pixel against the screen; this way they step together and it is
                // the map that scrolls past, a pixel of the screen at a time.
                _sceneCamera.Position = new Vector3(
                    player.X + MathF.Round((_exactCameraPosition.X - player.X) / CAMERA_PIXEL) * CAMERA_PIXEL,
                    player.Y + MathF.Round((_exactCameraPosition.Y - player.Y) / CAMERA_PIXEL) * CAMERA_PIXEL,
                    _exactCameraPosition.Z
                );
            }
            else
            {
                // As good as still (up against the edge of the map): it stays where it is, on the pixels of the screen
                _sceneCamera.Position = new Vector3(
                    MathF.Round(_exactCameraPosition.X / CAMERA_PIXEL) * CAMERA_PIXEL,
                    MathF.Round(_exactCameraPosition.Y / CAMERA_PIXEL) * CAMERA_PIXEL,
                    _exactCameraPosition.Z
                );
            }
            _cameraShake = Vector2.Zero;
        }
    }

    /// <summary>
    /// Helper method to have a player drawn where their body is right now, see FollowPlayers for why that can't wait.
    /// </summary>
    private static void SyncToPhysics(Player? player)
    {
        if (player?.PhysicsBody is { } body) player.Transform.Position = body.Position;
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
        _network?.Dispose();
        if (Network == _network) Network = null;
        if (Round == _round) Round = null;
        _occlusion?.Dispose();
        base.DisposeOther();
    }
}