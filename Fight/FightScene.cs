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
/// the rounds are run by the <see cref="RoundDirector"/> and everything under the scene finds the fight through its
/// <see cref="FightContext"/>. Everything about the fight that was settled before it (who, where, by what rules, online or
/// not) is the <see cref="MatchSetup"/> it was made from, which is also what makes it again for a rematch or a reload.
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

    /* What the setup settled, read off it. The rules the match is played by (online the host's, which came along with the
       map), the connection to the other machine, what gets the game files across, which corner is ours, who we play and
       who the dummy is, and the two knobs a test turns */

    private MatchRules Rules => _setup.Lobby?.StartedRules ?? _setup.Rules;
    private NetSession? Session => _setup.Session;
    private ContentSync? Content => _setup.Lobby?.Content;
    private bool StartsOnTheRight => _setup.Lobby is { LocalSide: 1 };
    private string? CharacterId => _setup.GetCharacter(0);
    private string? OpponentCharacterId => _setup.Mode == MatchMode.Practice && _setup.DummyCharacter.Length > 0 ? _setup.DummyCharacter : null;
    private (float One, float Two)? TestPositions => _setup.TestPositions;
    private DummyMode? StartingDummyMode => _setup.StartingDummyMode;

    // What the fight was set up as, the map it was asked for and the map as the rules have it (the weather), and where
    // the fight this one takes the place of was, null for one that starts from the top
    private readonly MatchSetup _setup;
    private readonly MapDefinition _map;
    private MapDefinition _mapDefinition;

    // The stage, loading on a worker since the fight was asked for, see the constructor
    private readonly Task<FightingStage> _stageLoading;
    private readonly FightResume? _resume;
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
    // The radius the arena was last told to blur to, see UpdateBackdropBlur. It starts out behind the versus screen
    private float _blurWanted = VERSUS_BLUR;

    // The lab, only for a fight against the dummy
    private TrainingMode? _training;
    private TrainingDrawer? _drawer;

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

        UpdateBackdropBlur();
        UpdateKnockOut(dt);
        UpdateReload(dt);

        // Our own content changed on the disk (saved in Hex), read again the way the pause menu's reload does it
        if (ContentWatcher.TakeSettled() && IsHost) ReloadData();

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
        if (!_setup.IsOnline) Engine.SceneManager.ResetTo(_setup.CreateFight(_map), Screen.IntoFight);
    }

    /// <summary>
    /// Called from the result card, back to the fight setup screen with the same setup (who has which gamepad, the
    /// fighters they had, the map and the rules), to pick again.
    /// </summary>
    private void ChangeFighters()
    {
        Engine.SceneManager.ResetTo(new FightSetupScene(_setup), Screen.IntoFight);
    }

    protected override void DisposeOther()
    {
        // Shut down sockets cleanly so ports aren't left hanging open. Unless the fight that replaced this one is using them
        if (!_handedOver) _network?.Dispose();

        _stage?.Dispose();

        // The time of the scene manager outlives the fight, a K.O. that was still slow mustn't leave the next scene slow
        if (_knockOutTime >= 0.0f) EndKnockOut();

        // Never set up (another scene was asked for before this one was shown), the stage it loaded was never put anywhere
        if (_stage is null && _stageLoading.IsCompletedSuccessfully)
        {
            _stageLoading.Result.Map.Dispose();
            _stageLoading.Result.Dispose();
        }

        base.DisposeOther();
    }
}
