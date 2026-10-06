using System;
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

using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Input2;
using Horizon.Physics;
using Horizon.Rendering;
using Horizon.Rendering.PostProcessing;
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

    // How far (in pixels) the arena is smeared behind the versus screen and behind the pause menu
    private const float VERSUS_BLUR = 14.0f;
    private const float PAUSE_BLUR = 10.0f;

    // How long (in seconds) the arena takes to come into focus once the versus screen is gone, and to blur and clear up around the pause menu
    private const float VERSUS_CLEAR_TIME = 0.6f;
    private const float PAUSE_BLUR_TIME = 0.16f;
    private const float PAUSE_CLEAR_TIME = 0.22f;

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

    /// <summary>
    /// Makes this same fight all over again, for a rematch. Null if there is none to be had, which also means no pause menu (its restart needs this).
    /// A fight can't just start itself over, its players and everything else in it belong to this scene and go when it does.
    /// </summary>
    public Func<FightScene>? Rematch { get; init; }

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

    // Smears the arena while something is up in front of it that wants the attention
    private BlurEffect _backdropBlur = null!;
    private PauseMenu? _pause;

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
        _world.RenderDebug = GameOptions.Hitboxes;

        // The camera goes first so it is up to date by the time anything is drawn with it
        Vector2 viewport = Engine.WindowManager.ViewportSize;
        ActiveCamera = _sceneCamera = AddEntity<Camera2D>(new(viewport / 2.0f));

        CreateRenderer(viewport);

        _stage = new FightingStage(_mapDefinition, _renderer, _world);
        Vector2 ourSpawn = StartsOnTheRight ? _stage.RightSpawn : _stage.LeftSpawn;
        Vector2 theirSpawn = StartsOnTheRight ? _stage.LeftSpawn : _stage.RightSpawn;

        var players = _renderer.AddEntity<SpriteBatch>();
        SpawnPlayerOne(players, ourSpawn);
        SpawnEffects();
        SpawnPlayerTwo(players, theirSpawn);

        // Both of them in the picture from the start
        _camera = new FightCamera(_sceneCamera, _stage, viewport, (ourSpawn + theirSpawn) / 2.0f);

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

        // Motion blur first and the CRT last, on top of everything the others did to the picture.
        // The fight starts out behind the versus screen, so the arena starts out as a smear
        Screen.AddMotionBlur(_renderer);
        _backdropBlur = _renderer.PostProcessing.Add(new BlurEffect { Radius = VERSUS_BLUR });
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

            // With a rematch on offer A is for going again and B for leaving, without one A just moves things along
            Rematch = Rematch is null ? null : StartRematch,
            RematchRequested = ConfirmPressed,
            SkipRequested = Rematch is null ? ConfirmPressed : BackPressed
        });

        _round.PhaseChanged += OnPhaseChanged;

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
        AddPauseMenu();
    }

    /// <summary>
    /// Helper method to give the fight its pause menu, which goes on top of the HUD. Online there is none, nobody gets to freeze somebody else's game.
    /// </summary>
    private void AddPauseMenu()
    {
        if (Session is not null || Rematch is null) return;

        _pause = AddComponent(new PauseMenu
        {
            // The versus screen and the result have the screen to themselves
            CanOpen = () => _round.Phase is RoundPhase.Ready or RoundPhase.Fight or RoundPhase.RoundOver,

            Opened = () => _backdropBlur.BlurTo(PAUSE_BLUR, PAUSE_BLUR_TIME),
            Closed = () => _backdropBlur.BlurTo(0.0f, PAUSE_CLEAR_TIME),

            Rematch = StartRematch,
            Quit = LeaveFight
        });
    }

    private void OnPhaseChanged(RoundPhase phase)
    {
        // The versus screen is gone, the arena comes into focus. It only ever leaves that phase the once
        if (phase != RoundPhase.Versus && _backdropBlur.Radius >= VERSUS_BLUR)
        {
            _backdropBlur.BlurTo(0.0f, VERSUS_CLEAR_TIME, Easing.InOutSine);
        }
    }

    public override void UpdateState(float dt)
    {
        // Nothing of the fight moves while the pause menu is up, only the menu itself
        if (_pause is { HoldsFight: true })
        {
            _pause.UpdateState(dt);
            return;
        }

        // Hits and thunder both rattle the view
        _camera.Shake(_weather.Shake + Fight.Effects.Shake);
        _playerLights.Follow(Fight.PlayerOne, Fight.PlayerTwo);

        base.UpdateState(dt);
    }

    public override void UpdatePhysics(float dt)
    {
        if (_pause is { HoldsFight: true }) return;

        base.UpdatePhysics(dt);

        // The players are drawn where the physics has just put them, and the camera follows in the same breath (see FightCamera.Follow)
        SyncToPhysics(Fight.PlayerOne);
        SyncToPhysics(Fight.PlayerTwo);

        if (Fight.PlayerOne is { } us && Fight.PlayerTwo is { } them)
        {
            _camera?.Follow(us.Transform.Position, them.Transform.Position, dt);
        }
    }

    private static void SyncToPhysics(Player? player)
    {
        if (player?.PhysicsBody is { } body) player.Transform.Position = body.Position;
    }

    /// <summary>
    /// Called by the rounds once the match is over and its result has been up for long enough, and by the pause menu for whoever has had enough.
    /// </summary>
    private void LeaveFight()
    {
        Engine.SetScene(new MainMenuScene(), Screen.IntoFight);
    }

    /// <summary>
    /// Called when the players want the same fight again, from the result of the match or from the pause menu.
    /// </summary>
    private void StartRematch()
    {
        if (Rematch is not null) Engine.SetScene(Rematch(), Screen.IntoFight);
    }

    /// <summary>
    /// Helper method to test whether anybody has pressed a button that says yes.
    /// </summary>
    private static bool ConfirmPressed()
    {
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (gamepad.IsConnected && MenuInput.ConfirmPressed(gamepad)) return true;
        }

        return false;
    }

    /// <summary>
    /// Helper method to test whether anybody has pressed the button that says no.
    /// </summary>
    private static bool BackPressed()
    {
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (gamepad.IsConnected && gamepad.WasPressed(GamepadInput.B)) return true;
        }

        return false;
    }

    protected override void DisposeOther()
    {
        // Shut down sockets cleanly so ports aren't left hanging open
        _network?.Dispose();

        // Only if the fight is still ours. By the time a scene is disposed of the next one is set up, and after a rematch that is another fight
        if (Fight.Round == _round) Fight.Clear();

        _stage?.Dispose();
        base.DisposeOther();
    }
}
