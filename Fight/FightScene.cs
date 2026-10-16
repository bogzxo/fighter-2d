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


/// <summary>
/// Main arena scene where two absolute units crash out against each other.
/// The scene only wires things together. The map is the <see cref="FightingStage"/>, the camera the <see cref="FightCamera"/>,
/// the rounds are run by the <see cref="RoundDirector"/> and everything else finds the fight through <see cref="Fight"/>.
/// </summary>
internal partial class FightScene : Scene
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
    /// Which character the dummy is, for a practice fight. Null for the first one there is, and nothing at all when player two is a person.
    /// </summary>
    public string? OpponentCharacterId { get; init; }

    /// <summary>
    /// Where the two players stand at the start of every round, in units of the world across the stage, each facing
    /// the other. Null for the spawn of the map (both on it, pushed apart by their bodies, which lands them somewhere a
    /// little different every time). For tests that have to start from the same place every time (--positions).
    /// </summary>
    public (float One, float Two)? TestPositions { get; init; }

    /// <summary>
    /// What the dummy does from the very first tick, rather than fighting until somebody changes it in the pause menu
    /// (by which time it has dashed off somewhere). For tests (--dummy stand). Null for the usual, it fights.
    /// </summary>
    public DummyMode? StartingDummyMode { get; init; }

    /// <summary>
    /// Called with the id of the character the dummy should be from now on, when the training menu changes it. Whoever makes the fight
    /// keeps the choice, the fight itself is built again from where it was (the way a reload does it) with the new dummy in it.
    /// </summary>
    public Action<string>? OpponentChanged { get; init; }

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

    // The fight itself as everything under the scene sees it, the players, the stage, the effects, the rounds
    private FightContext _fight = null!;

    // The two of them and the effects as the scene knows them, the context only knows what the simulation needs of them
    private Player _one = null!, _two = null!;
    private FightEffects _effects = null!;

    // Host only, after a reload. Whether the fight is still held for the other machine to be done reloading as well,
    // how long it has been, and how long until it is asked again
    private bool _awaitingPeer;
    private float _awaitTime, _askTimer;

    public override void UpdateState(float dt)
    {
        if (_resumePending) ResumeFighters();
        if (_awaitingPeer) AwaitPeer(dt);

        UpdatePauseBlur();
        UpdateReload(dt);

        // Our own content changed on the disk (saved in Hex), read again the way the pause menu's reload does it
        if (ContentWatcher.TakeSettled() && IsHost && Reload is not null) ReloadData();

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
            // Under the options the menu is still up and nobody is pressing anything on it
            _pause.Quiet = IsSuspended || IsLeaving;

            _hud.UpdateState(dt);
            _pause.UpdateState(dt);
            _transfer?.UpdateState(dt);
            return;
        }

        // Hits and thunder both rattle the view
        _camera.Shake(_weather.Shake + _effects.Shake);
        _playerLights.Follow(_one, _two);
        _stage.Tick(dt);

        // The lab tops the health back up once the dust has settled, only while the round is actually on
        if (_round.Phase == RoundPhase.Fight) _training?.Update(dt);

        base.UpdateState(dt);
    }

    public override void UpdatePhysics(float dt)
    {
        if (_pause is { HoldsFight: true } || _awaitingPeer) return;

        base.UpdatePhysics(dt);

        if (_one is { } us && _two is { } them)
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
        // The lot, should the fight be under something (the options) when it ends, the other machine hanging up say
        Engine.SceneManager.ResetTo(new MainMenuScene(), Screen.IntoFight);
    }

    /// <summary>
    /// Called when the players want the same fight again, from the result of the match or from the pause menu.
    /// </summary>
    private void StartRematch()
    {
        if (Rematch is not null) Engine.SceneManager.ResetTo(Rematch(), Screen.IntoFight);
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

        _stage?.Dispose();
        base.DisposeOther();
    }
}
