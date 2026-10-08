using Bogz.Logging;
using System;
using System.Numerics;

using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Input;
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

    // How long (in seconds) the host waits for the other machine to say it has the data before a reload is called off,
    // and how long it holds the reloaded fight for the other machine to be back in it before it carries on regardless
    private const float RELOAD_PATIENCE = 8.0f;

    // How often (in seconds) the host asks the other machine whether it is done reloading
    private const float RELOAD_ASK_INTERVAL = 0.5f;

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

    /// <summary>
    /// Makes this same fight again from where it was, with everything read from disk again. For the host tools of the pause menu.
    /// </summary>
    public Func<FightResume, FightScene>? Reload { get; init; }

    /// <summary>
    /// Where the fight this one takes the place of was, null for a fight that starts from the top.
    /// </summary>
    public FightResume? Resume { get; init; }

    /// <summary>
    /// What gets the game files from the host to the other machine, for an online fight.
    /// </summary>
    public ContentSync? Content { get; init; }

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
    private HUDManager _hud = null!;
    private PauseMenu? _pause;
    private bool _pauseBlurred;

    // The lab, only for a fight against the dummy
    private TrainingMode? _training;

    // Reloading the data. How long the host still waits for the other machine to have it, the panel that shows the files going across,
    // whether the fighters still have to be put back where they were, and whether the connection went on to the fight that replaces this one
    private float _reloadWait;
    private ContentTransferDisplay? _transfer;
    private bool _resumePending;
    private bool _handedOver;

    // Host only, after a reload. Whether the fight is still held for the other machine to be done reloading as well,
    // how long it has been, and how long until it is asked again
    private bool _awaitingPeer;
    private float _awaitTime, _askTimer;

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

        Fight.Stage = _stage = new FightingStage(_mapDefinition, _renderer, _world);
        Vector2 ourSpawn = StartsOnTheRight ? _stage.RightSpawn : _stage.LeftSpawn;
        Vector2 theirSpawn = StartsOnTheRight ? _stage.LeftSpawn : _stage.RightSpawn;

        var players = _renderer.AddEntity<SpriteBatch>();
        SpawnPlayerOne(players, ourSpawn);
        SpawnEffects();
        SpawnPlayerTwo(players, theirSpawn);

        // Both of them in the picture from the start
        _camera = new FightCamera(_sceneCamera, _stage, viewport, (ourSpawn + theirSpawn) / 2.0f);

        StartMatch();
        ResumeMatch();

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
        Screen.AddMotionBlur(_renderer, _sceneCamera);
        _backdropBlur = _renderer.PostProcessing.Add(new BlurEffect { Radius = VERSUS_BLUR });
        Screen.AddCrt(_renderer, FightCamera.Zoom);
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
        // Somebody on this machine, somebody on another one, or the dummy if nobody showed up. A fight against the dummy is the lab
        if (_opponent is null)
        {
            var dummy = new DummyPlayerInput();
            _opponent = new Player { Controller = new PlayerController(dummy) };
            _training = new TrainingMode(dummy, Fight.PlayerOne, _opponent);
        }

        Fight.PlayerTwo = _opponent;
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
                RemotePlayer = Fight.PlayerTwo,
                FightHeld = () => _pause is { HoldsFight: true } || _awaitingPeer
            });
        }

        // The calls across the screen make way for the pause countdown, which lands in the same spot
        _hud = AddComponent(new HUDManager { BannerHidden = () => _pause is { HoldsFight: true } });
        AddPauseMenu();

        if (Content is not null) _transfer = AddComponent(new ContentTransferDisplay(Content, Session is { IsHost: true }));
        if (_network is not null) _network.ReloadRequested = ReloadNow;
    }

    /// <summary>
    /// Helper method to give the fight its pause menu, which goes on top of the HUD.
    /// Online a pause holds both machines still, each tells the other when it pauses and when it carries on.
    /// </summary>
    private void AddPauseMenu()
    {
        _pause = AddComponent(new PauseMenu
        {
            // The versus screen and the result have the screen to themselves
            CanOpen = () => _round.Phase is RoundPhase.Ready or RoundPhase.Fight or RoundPhase.RoundOver,

            Opened = () => _network?.SendPause(true),
            Closed = () => _network?.SendPause(false),
            HeldByOther = _network is { } network ? () => network.RemotePaused : null,

            // Starting over is for fights on one machine, the other one would have to want it as well
            Rematch = Rematch is null || Session is not null ? null : StartRematch,
            Quit = LeaveFight,

            // The tools are the host's. On one machine that is whoever is playing
            ReloadData = IsHost && Reload is not null ? ReloadData : null,
            ToggleHitboxes = IsHost ? () => _world.RenderDebug = !_world.RenderDebug : null,

            Training = _training
        });
    }

    private bool IsHost => Session is null || Session.IsHost;

    /* Reloading the data in the middle of a fight */

    /// <summary>
    /// Called from the host tools of the pause menu. Everything the fight is made of (moves, characters, maps, art) is read from disk again.
    /// Online the other machine gets whatever changed first and then does the same, so both of them carry on with the same data.
    /// </summary>
    private void ReloadData()
    {
        if (_reloadWait > 0.0f) return;

        if (_network is null || Content is null)
        {
            ReloadNow();
            return;
        }

        // Offering the content again has the other machine fetch what is different, and say so once it has all of it
        Content.Offer();
        _reloadWait = RELOAD_PATIENCE;
        if (_transfer is not null) _transfer.Waiting = "Reloading the data";
    }

    /// <summary>
    /// Helper method for the host to wait until the other machine has the data, and to tell it to reload once it does.
    /// </summary>
    private void UpdateReload(float dt)
    {
        if (_reloadWait <= 0.0f || Content is null) return;

        if (Content.PeerVerified)
        {
            _network?.SendReload();
            ReloadNow();
            return;
        }

        // Files that are still going across are no reason to give up, only silence is
        if (!Content.IsTransferring) _reloadWait -= dt;
        if (_reloadWait > 0.0f) return;

        Log.Warning("[Fight] The other machine never said it has the data, nothing was reloaded.");
        if (_transfer is not null) _transfer.Waiting = string.Empty;
    }

    /// <summary>
    /// Helper method to swap this fight for the same one built from what is on disk right now, carrying on from where this one is.
    /// </summary>
    private void ReloadNow()
    {
        if (Reload is null || _handedOver) return;

        Log.Info("[Fight] Reading the data again.");

        // What was worked out from the old files is no good for the new ones
        CharacterArt.Forget();

        var resume = new FightResume(_round.TakeSnapshot(), FighterResume.Of(Fight.PlayerOne), FighterResume.Of(Fight.PlayerTwo));

        // The fight that takes over uses the same connection, so this one lets go of it without hanging up
        _network?.Detach();
        _handedOver = true;

        Engine.SetScene(Reload(resume), null);
    }

    /// <summary>
    /// Helper method for a fight that takes the place of another one. The match goes back to how it stood,
    /// and the arena is in focus from the start because there is no versus screen to sit behind.
    /// </summary>
    private void ResumeMatch()
    {
        if (Resume is not { } resume) return;

        _round.Restore(resume.Round);
        _resumePending = true;

        if (resume.Round.Phase != RoundPhase.Versus)
        {
            _backdropBlur.Radius = 0.0f;
            _backdropBlur.Enabled = false;
        }

        // The host doesn't get a head start, it holds still until the other machine is back in the fight too
        _awaitingPeer = Session is { IsHost: true };
    }

    /// <summary>
    /// Helper method for the host to wait for the other machine after a reload. It asks every so often,
    /// and gives up waiting if they left or take forever.
    /// </summary>
    private void AwaitPeer(float dt)
    {
        _awaitTime += dt;

        bool done = _network is null || _network.PeerReloaded || !_network.IsConnected || _awaitTime >= RELOAD_PATIENCE;
        if (done)
        {
            if (_network is { PeerReloaded: false }) Log.Warning("[Fight] Never heard that the other machine is done reloading, carrying on anyway.");

            _awaitingPeer = false;
            if (_transfer is not null) _transfer.Waiting = string.Empty;

            // Both of us are back, three two one and off we go
            _pause?.StartCountdown();
            return;
        }

        if ((_askTimer -= dt) <= 0.0f)
        {
            _askTimer = RELOAD_ASK_INTERVAL;
            _network!.AskReloaded();
        }

        if (_transfer is not null)
        {
            _transfer.Waiting = "Data reloaded";
            _transfer.WaitingDetail = "waiting for the other machine to do the same";
            _transfer.WaitingProgress = 1.0f;
        }
    }

    /// <summary>
    /// Helper method to put the fighters back the way they were, once they have bodies to put anywhere.
    /// </summary>
    private void ResumeFighters()
    {
        if (Resume is not { } resume || Fight.PlayerOne.PhysicsBody is null || Fight.PlayerTwo.PhysicsBody is null) return;
        _resumePending = false;

        Place(Fight.PlayerOne, resume.One);
        Place(Fight.PlayerTwo, resume.Two);

        // That is us back in the fight, which the host is waiting to hear. It counts down from there, and so do we.
        // On one machine there is nobody to wait for
        if (Session is { IsHost: false }) _network?.SayReloaded();
        if (!_awaitingPeer) _pause?.StartCountdown();

        static void Place(Player player, FighterResume was)
        {
            player.Health = was.Health;
            player.PhysicsBody.Position = was.Position;
            player.Transform.Position = was.Position;
            player.Transform.Snap();
        }
    }

    /// <summary>
    /// Helper method to smear the arena for as long as there is a pause on screen, ours or the other machine's.
    /// </summary>
    private void UpdatePauseBlur()
    {
        bool paused = _pause is { IsShowing: true } || _awaitingPeer;
        if (paused == _pauseBlurred) return;

        _pauseBlurred = paused;

        if (paused) _backdropBlur.BlurTo(PAUSE_BLUR, PAUSE_BLUR_TIME);
        else _backdropBlur.BlurTo(0.0f, PAUSE_CLEAR_TIME);
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
        if (_resumePending) ResumeFighters();
        if (_awaitingPeer) AwaitPeer(dt);

        UpdatePauseBlur();
        UpdateReload(dt);

        // Held for the other machine to catch up after a reload, which is a pause nobody asked for.
        // The HUD is kept going through every kind of hold. A fight that comes back from a reload is held from its first update,
        // and a HUD that was never updated has nothing to draw
        if (_awaitingPeer)
        {
            _hud.UpdateState(dt);
            _transfer?.UpdateState(dt);
            return;
        }

        // Nothing of the fight moves while it is paused (by us or by the other machine), only the pause itself
        // and the files that may be going across for a reload
        if (_pause is { HoldsFight: true })
        {
            _hud.UpdateState(dt);
            _pause.UpdateState(dt);
            _transfer?.UpdateState(dt);
            return;
        }

        // Hits and thunder both rattle the view
        _camera.Shake(_weather.Shake + Fight.Effects.Shake);
        _playerLights.Follow(Fight.PlayerOne, Fight.PlayerTwo);

        // The lab tops the health back up once the dust has settled, only while the round is actually on
        if (_round.Phase == RoundPhase.Fight) _training?.Update(dt);

        base.UpdateState(dt);
    }

    public override void UpdatePhysics(float dt)
    {
        if (_pause is { HoldsFight: true } || _awaitingPeer) return;

        base.UpdatePhysics(dt);

        if (Fight.PlayerOne is { } us && Fight.PlayerTwo is { } them)
        {
            // They walk through each other as far as the physics goes, this is what keeps them apart
            Pushboxes.Separate(us, them, dt);

            _camera?.Follow(us.Transform.Position, them.Transform.Position, dt);
        }
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
        // Shut down sockets cleanly so ports aren't left hanging open. Unless the fight that replaced this one is using them
        if (!_handedOver) _network?.Dispose();

        // Only if the fight is still ours. By the time a scene is disposed of the next one is set up, and after a rematch that is another fight
        if (Fight.Round == _round) Fight.Clear();

        _stage?.Dispose();
        base.DisposeOther();
    }
}
