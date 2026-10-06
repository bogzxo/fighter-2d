using System.Numerics;

using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.Character.Controllers.Dummy;
using Fighter2D.Combat;
using Fighter2D.Effects;
using Fighter2D.HUD;
using Fighter2D.Map;
using Fighter2D.Match;
using Fighter2D.Networking;

using Horizon.Engine;
using Horizon.Input2;
using Horizon.Physics;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Scenes;

/// <summary>
/// Main arena scene where two absolute units crash out against each other.
/// The scene only wires things together. The map is the <see cref="FightingStage"/>, the camera the <see cref="FightCamera"/>,
/// the rounds are run by the <see cref="RoundDirector"/> and everything else finds the fight through <see cref="Fight"/>.
/// </summary>
internal class FightScene : Scene
{
    private static readonly Vector2 GRAVITY = new(0, -8000);

    public override Camera ActiveCamera { get; protected set; } = null!;

    /// <summary>
    /// The rules the match is played by, the defaults if nobody set any.
    /// </summary>
    public MatchRules Rules { get; init; } = new();

    /// <summary>
    /// The connection to the other machine, for an online fight.
    /// </summary>
    public NetSession? Session { get; init; }

    /// <summary>
    /// Whether our player starts in the right corner instead of the left.
    /// </summary>
    public bool StartsOnTheRight { get; init; }

    /// <summary>
    /// The character our player picked, null for the first one there is.
    /// </summary>
    public string? CharacterId { get; init; }

    private MapDefinition _mapDefinition;
    private readonly int _gamepadIndex;
    private Player? _opponent;

    private PhysicsWorld _world = null!;
    private DeferredRenderer2D _renderer = null!;
    private Camera2D _sceneCamera = null!;
    private FightingStage _stage = null!;
    private FightCamera _camera = null!;
    private Weather _weather = null!;
    private PlayerLights _playerLights = null!;
    private RoundDirector _round = null!;
    private FightNetwork? _network;

    /// <param name="opponent">Whoever player two is, null to fight the dummy.</param>
    public FightScene(MapDefinition mapDefinition, int gamepadIndex, Player? opponent = null)
    {
        _mapDefinition = mapDefinition;
        _gamepadIndex = gamepadIndex;
        _opponent = opponent;
    }

    public override void Initialize()
    {
        // The rules get a say in what the map is like (its weather) before anything is made out of it
        _mapDefinition = Rules.Apply(_mapDefinition);

        // Spin up physics world with heavy gravity so players wont float around like fuckin astronauts
        _world = AddComponent<PhysicsWorld>();
        _world.Gravity = GRAVITY;

        // The camera goes first so it is up to date by the time anything is drawn with it
        Vector2 viewport = Engine.WindowManager.ViewportSize;
        ActiveCamera = _sceneCamera = AddEntity<Camera2D>(new(viewport / 2.0f));

        CreateRenderer(viewport);

        _stage = new FightingStage(_mapDefinition, _world, _renderer);
        Vector2 ourSpawn = StartsOnTheRight ? _stage.RightSpawn : _stage.LeftSpawn;
        Vector2 theirSpawn = StartsOnTheRight ? _stage.LeftSpawn : _stage.RightSpawn;

        var players = _renderer.AddEntity<SpriteBatch>();
        SpawnPlayerOne(players, ourSpawn);
        SpawnEffects();
        SpawnPlayerTwo(players, theirSpawn);

        _camera = new FightCamera(_sceneCamera, _stage, viewport, ourSpawn);

        StartMatch();

        base.Initialize();
    }

    /// <summary>
    /// Helper method to create the renderer of the world. Whatever is added to it gets lit, the HUD stays with the scene and is drawn over the top.
    /// </summary>
    private void CreateRenderer(Vector2 viewport)
    {
        _renderer = AddEntity(new DeferredRenderer2D((uint)viewport.X, (uint)viewport.Y)
        {
            Ambient = _mapDefinition.Lighting.Ambient,

            // The art is one world unit per pixel drawn at twice that, and the lighting follows the art
            LightingPixelSize = 1.0f
        });

        // Blur first, then the CRT on top of the blurred picture
        Screen.AddMotionBlur(_renderer);
        Screen.AddCrt(_renderer);
    }

    private void SpawnPlayerOne(SpriteBatch players, Vector2 spawn)
    {
        Fight.PlayerOne = new Player()
        {
            Controller = new PlayerController(new GamepadPlayerInput(_gamepadIndex)),
            SpawnPosition = spawn,
            CharacterId = CharacterId
        };

        players.Add(AddEntity(Fight.PlayerOne));
    }

    private void SpawnPlayerTwo(SpriteBatch players, Vector2 spawn)
    {
        // Somebody on this machine, somebody on another one, or the dummy if nobody showed up
        Fight.PlayerTwo = _opponent ?? new Player()
        {
            Controller = new PlayerController(new DummyPlayerInput())
        };

        Fight.PlayerTwo.SpawnPosition = spawn;
        players.Add(AddEntity(Fight.PlayerTwo));
    }

    /// <summary>
    /// Helper method to add everything that flies, falls and glows. It is all drawn over the players.
    /// </summary>
    private void SpawnEffects()
    {
        Fight.Effects = _renderer.AddEntity(new FightEffects(_world, _renderer));
        _weather = _renderer.AddEntity(new Weather(_sceneCamera, _mapDefinition, _world, _renderer));

        _stage.SpawnEmitters(Fight.Effects);
        _stage.SpawnLights(_renderer);
        _playerLights = new PlayerLights(_renderer, _mapDefinition.Lighting.Ambient);

        // Whatever of the map is marked as foreground goes over the players and the effects
        _renderer.AddEntity(_stage.Map.Foreground);
    }

    /// <summary>
    /// Helper method to set up the rounds, the netcode and the HUD, in that order because each needs the one before.
    /// </summary>
    private void StartMatch()
    {
        Fight.CombatLog = new CombatLog();

        // Online it is the host that runs the rounds, the other machine is told how they go
        Fight.Round = _round = AddComponent(new RoundDirector(Rules, Fight.PlayerOne, Fight.PlayerTwo)
        {
            IsAuthority = Session is null || Session.IsHost,
            Finished = LeaveFight,
            SkipRequested = ContinuePressed
        });

        Fight.Network = null;
        if (Session is not null)
        {
            Fight.Network = _network = AddComponent(new FightNetwork(Session, _round)
            {
                LocalPlayer = Fight.PlayerOne,
                RemotePlayer = Fight.PlayerTwo
            });
        }

        AddComponent<HUDManager>();
    }

    public override void UpdateState(float dt)
    {
        // Hits and thunder both rattle the view
        _camera.Shake(_weather.Shake + Fight.Effects.Shake);
        _playerLights.Follow(Fight.PlayerOne, Fight.PlayerTwo);

        base.UpdateState(dt);
    }

    public override void UpdatePhysics(float dt)
    {
        base.UpdatePhysics(dt);

        // The players are drawn where the physics has just put them, and the camera follows in the same breath (see FightCamera.Follow)
        SyncToPhysics(Fight.PlayerOne);
        SyncToPhysics(Fight.PlayerTwo);

        if (Fight.PlayerOne is { } player) _camera?.Follow(player.Transform.Position, dt);
    }

    private static void SyncToPhysics(Player? player)
    {
        if (player?.PhysicsBody is { } body) player.Transform.Position = body.Position;
    }

    /// <summary>
    /// Called by the rounds once the match is over and its result has been up for long enough.
    /// </summary>
    private void LeaveFight()
    {
        Engine.SetScene(new MainMenuScene());
    }

    /// <summary>
    /// Helper method to test whether anybody has pressed the button that skips the result screen.
    /// </summary>
    private static bool ContinuePressed()
    {
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            if (gamepad.WasPressed(GamepadInput.A) || gamepad.WasPressed(GamepadInput.Start)) return true;
        }

        return false;
    }

    protected override void DisposeOther()
    {
        // Shut down sockets cleanly so ports aren't left hanging open
        _network?.Dispose();

        if (Fight.Network == _network) Fight.Network = null;
        if (Fight.Round == _round) Fight.Round = null;

        _stage?.Dispose();
        base.DisposeOther();
    }
}
