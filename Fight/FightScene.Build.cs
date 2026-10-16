using Horizon.Content;
using Horizon.Logging;
using System;
using System.Numerics;

using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Engine.Components;
using Horizon.Input;
using Horizon.Physics;
using Horizon.Rendering;
using Horizon.Rendering.PostProcessing;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Fight;

// How a fight is made, in order. The world, the renderer, the stage, the two players, the effects, the rounds, the
// netcode, the HUD and the pause menu, each needing the one before. What the scene does once it is up is in
// FightScene.cs, reloading the data in FightScene.Reload.cs and the pause in FightScene.Pause.cs
internal partial class FightScene
{
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

        // Made before anything that is part of it, the players are handed it as they are made
        _fight = new FightContext();

        // Spin up physics world with heavy gravity so players wont float around like fuckin astronauts
        _world = AddComponent<PhysicsWorld>();
        _world.Gravity = GRAVITY;
        _world.RenderDebug = GameOptions.Hitboxes;

        // The camera goes first so it is up to date by the time anything is drawn with it
        Vector2 viewport = Engine.WindowManager.ViewportSize;
        ActiveCamera = _sceneCamera = AddEntity<Camera2D>(new(viewport / 2.0f));

        CreateRenderer(viewport);

        _fight.Stage = _stage = new FightingStage(_mapDefinition, _renderer, _world);
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
            Lighting = Screen.WorldLighting
        });

        // How it shades, the highlights, the shadows, how dark the corners go, is all in DeferedStyle
        DeferedStyle.Apply(_renderer);

        // The glow round the lanterns first, then the CRT last, on top of everything the others did to the picture.
        // The fight starts out behind the versus screen, so the arena starts out as a smear
        Screen.AddBloom(_renderer);
        _backdropBlur = _renderer.PostProcessing.Add(new BlurEffect { Radius = VERSUS_BLUR });
        Screen.AddCrt(_renderer, FightCamera.Zoom);
    }

    private void SpawnPlayerOne(SpriteBatch players, Vector2 spawn)
    {
        _one = new Player()
        {
            Fight = _fight,
            Controller = new PlayerController(new GamepadPlayerInput(_gamepadIndex)),
            SpawnPosition = TestPositions is { } placed ? spawn with { X = placed.One } : spawn,
            StartFacing = TestPositions is { } facing ? MathF.Sign(facing.Two - facing.One) : null,
            CharacterId = CharacterId
        };

        _fight.PlayerOne = _one;
        players.Add(AddEntity(_one));
    }

    private void SpawnPlayerTwo(SpriteBatch players, Vector2 spawn)
    {
        // Somebody on this machine, somebody on another one, or the dummy if nobody showed up. A fight against the dummy is the lab
        if (_opponent is null)
        {
            var dummy = new DummyPlayerInput();
            if (StartingDummyMode is { } mode) dummy.Mode = mode;
            _opponent = new Player { Controller = new PlayerController(dummy), CharacterId = OpponentCharacterId };
            _training = new TrainingMode(dummy, _one, _opponent)
            {
                // Swapping the dummy for another character is a reload with a different player two
                ChangeOpponent = OpponentChanged is null || Reload is null ? null : ChangeOpponent
            };
        }

        _opponent.Fight = _fight;
        _two = _opponent;
        _fight.PlayerTwo = _two;
        _two.SpawnPosition = TestPositions is { } placed ? spawn with { X = placed.Two } : spawn;
        if (TestPositions is { } facing) _two.StartFacing = MathF.Sign(facing.One - facing.Two);
        players.Add(AddEntity(_two));
    }

    /// <summary>
    /// Helper method to add everything that flies, falls and glows. It is all drawn over the players.
    /// </summary>
    private void SpawnEffects()
    {
        _fight.Effects = _effects = _renderer.AddEntity(new FightEffects(_world, _renderer));
        _weather = _renderer.AddEntity(new Weather(_sceneCamera, _mapDefinition, _world, _renderer));

        _stage.SpawnEmitters(_effects);
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
        _fight.CombatLog = new CombatLog();

        // Online it is the host that runs the rounds, the other machine is told how they go
        _fight.Round = _round = AddComponent(new RoundDirector(Rules, _one, _two)
        {
            IsAuthority = Session is null || Session.IsHost,
            Finished = LeaveFight,

            // With a rematch on offer A is for going again and B for leaving, without one A just moves things along
            Rematch = Rematch is null ? null : StartRematch,
            RematchRequested = ConfirmPressed,
            SkipRequested = Rematch is null ? ConfirmPressed : BackPressed
        });

        _round.PhaseChanged += OnPhaseChanged;

        _fight.Network = null;
        if (Session is not null)
        {
            _fight.Network = _network = AddComponent(new FightNetwork(Session, _round)
            {
                LocalPlayer = _one,
                RemotePlayer = _two,
                FightHeld = () => _pause is { HoldsFight: true } || _awaitingPeer
            });
        }

        // The calls across the screen make way for the pause countdown, which lands in the same spot
        _hud = AddComponent(new HUDManager { Fight = _fight, Round = _round, BannerHidden = () => _pause is { HoldsFight: true } });
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

            Options = OpenOptions,

            // The tools are the host's. On one machine that is whoever is playing
            ReloadData = IsHost && Reload is not null ? ReloadData : null,
            ToggleHitboxes = IsHost ? () => _world.RenderDebug = !_world.RenderDebug : null,

            Training = _training
        });
    }

    private bool IsHost => Session is null || Session.IsHost;
}
