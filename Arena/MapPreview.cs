using System.Numerics;

using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Physics;
using Horizon.Rendering;

namespace Fighter2D.Arena;

/// <summary>
/// A live picture of a map, behind the fight setup screen. It is the real thing and not a screenshot, the same tiles, lights and weather a fight on it would have.
/// Nobody is standing on it so it has no collision, and the camera just drifts back and forth over where the fight would start.
/// Ask for a map with <see cref="Show"/> from wherever. It is loaded on a worker while the one before it stays up, then
/// swapped in at the start of a frame and the one before it thrown away, so flicking through the maps never stops the game.
/// </summary>
internal sealed class MapPreview : GameObject
{
    // How far (in world units) the camera drifts to either side, and how long (in seconds) one trip there and back takes
    private const float DRIFT = 36.0f;
    private const float DRIFT_TIME = 6.0f;

    // How long the first map takes to come up out of the dark, rather than popping in over a black screen
    private const float FADE_IN = 0.45f;

    private readonly Camera2D _camera;
    private readonly PhysicsWorld _world;
    private readonly Vector2 _viewportSize;
    private readonly Vector2 _focusOffset;
    private readonly TweenContext _tweens = new();

    private DeferredRenderer2D _renderer = null!;

    // The map somebody asked for and the one that is showing, by its file name
    private MapDefinition _wanted;
    private string? _shownFile;

    // The map being loaded on a worker and what it was asked for as, one at a time. A map asked for while another one
    // loads waits for it, they take a tenth of a second, and the one that came too late is thrown away
    private Task<FightingStage>? _loading;
    private MapDefinition _loadingMap;

    private FightingStage? _stage;
    private Weather? _weather;
    private FightCamera? _view;

    // Where the camera is looking before the drift, and how far along the drift it is from -1 to 1
    private Vector2 _focus;
    private float _drift = -1.0f;

    /// <param name="camera">The camera the map is seen through. It has to be the active camera of the scene, the tile map and the lighting go by that.</param>
    /// <param name="world">A physics world for the weather to live in. It can be empty.</param>
    /// <param name="focusOffset">How far from the middle of the two spawns the camera looks (in world units), for a screen that has a menu covering part of it.</param>
    public MapPreview(Camera2D camera, PhysicsWorld world, Vector2 viewportSize, Vector2 focusOffset)
    {
        Name = "Map Preview";

        _camera = camera;
        _world = world;
        _viewportSize = viewportSize;
        _focusOffset = focusOffset;
    }

    public override void Initialize()
    {
        base.Initialize();

        _renderer = AddEntity(new DeferredRenderer2D((uint)_viewportSize.X, (uint)_viewportSize.Y)
        {
            Lighting = Screen.WorldLighting
        });

        // Shaded the way the fight is, glow and all, or the preview is a lie
        WorldStyle.Apply(_renderer);
        Screen.AddBloom(_renderer);

        // Back and forth forever, slowly
        _tweens.Play(Tween.To(() => _drift, drift => _drift = drift, 1.0f, DRIFT_TIME / 2).SetEasing(Easing.InOutSine).SetLoops(-1, LoopMode.PingPong));

        GameEngine.Instance.WindowManager.Exclusive += Swap;
    }

    /// <summary>
    /// Asks for a map to be shown. Can be called from any thread.
    /// </summary>
    public void Show(MapDefinition map) => _wanted = map;

    /// <summary>The map that is up, null until the first one has loaded (on a worker).</summary>
    public FightingStage? Stage => _stage;



    public override void UpdateState(float dt)
    {
        _stage?.Tick(dt);

        // A map that is done loading is swapped in with the game standing still (see Swap), and the map that was
        // asked for is started on if nothing is loading
        if (_loading is { IsCompleted: true })
            GameEngine.Instance.WindowManager.RequestExclusive();
        else if (_loading is null && _wanted.FileName is { } file && file != _shownFile)
            StartLoading(_wanted);

        _tweens.Tick(dt);
        _view?.LookAt(_focus + new Vector2(_drift * DRIFT, 0.0f));

        base.UpdateState(dt);
    }

    // A method of its own, a lambda over a parameter of UpdateState would make the object it closes over every tick
    private void StartLoading(MapDefinition map)
    {
        _loadingMap = map;
        _loading = Task.Run(() => FightingStage.Load(map, walked: false));
    }

    /// <summary>
    /// Helper method to put the map that finished loading in place of the one that is showing. It changes what the
    /// preview is made of and hands the renderer a new occlusion, so it happens on the render thread at the start of a
    /// frame with the game standing still (halfway through drawing a frame that goes tits up). Everything slow was
    /// done on the worker, this is a few entities moved about, and the old map is thrown away once the new one is in.
    /// </summary>
    private void Swap(float dt)
    {
        if (IsDisposed || _loading is not { IsCompleted: true } loading) return;

        _loading = null;
        MapDefinition map = _loadingMap;

        if (!loading.IsCompletedSuccessfully)
        {
            Horizon.Logging.Log.Error($"[MapPreview] Couldn't load '{map.FileName}', {loading.Exception?.GetBaseException().Message}");
            // Not tried again every tick, only once it's asked for again after another one
            if (map.FileName == _wanted.FileName) _shownFile = map.FileName;
            return;
        }

        FightingStage stage = loading.Result;

        // Somebody moved on while it loaded, the one they want now is started on the next tick
        if (map.FileName != _wanted.FileName)
        {
            Discard(stage);
            return;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool first = _stage is null;
        Unload();
        Attach(stage, map);
        if (first) FadeIn(stage);
        _shownFile = map.FileName;
        Horizon.Logging.Log.Info($"[MapPreview] Swapped in '{map.FileName}' in {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms, with the game standing still for it.");
    }

    protected override void DisposeOther()
    {
        GameEngine.Instance.WindowManager.Exclusive -= Swap;

        // One that finished is thrown away here, one still loading was never drawn and has nothing on the GPU, the
        // garbage collector can have it
        if (_loading is { IsCompletedSuccessfully: true } loaded) Discard(loaded.Result);
        _loading = null;

        base.DisposeOther();
    }

    private void Attach(FightingStage stage, MapDefinition map)
    {
        // The ambient light goes first, the storm of the weather remembers it as what to go back to after a flash
        _renderer.Ambient = map.Lighting.Ambient;

        _stage = stage;
        _stage.Attach(_renderer, world: null);
        _stage.SpawnLights(_renderer);

        _weather = _renderer.AddEntity(new Weather(_camera, map, _world, _renderer));
        _renderer.AddEntity(_stage.Map.Foreground);

        _focus = (_stage.LeftSpawn + _stage.RightSpawn) / 2.0f + _focusOffset;
        _view = new FightCamera(_camera, _stage, _viewportSize, _focus);
    }

    // A method of its own, Swap runs every frame and a lambda over its stage would be an object every frame
    private void FadeIn(FightingStage stage)
    {
        stage.Map.Opacity = 0.0f;
        _tweens.Play(Tween.To(() => stage.Map.Opacity, opacity => stage.Map.Opacity = opacity, 1.0f, FADE_IN).SetEasing(Easing.OutCubic));
    }

    private static void Discard(FightingStage stage)
    {
        stage.Map.Dispose();
        stage.Dispose();
    }

    /// <summary>
    /// Helper method to take the map that is showing out again, with everything that came with it.
    /// </summary>
    private void Unload()
    {
        _view = null;

        if (_weather is not null)
        {
            _renderer.RemoveEntity(_weather);
            _weather.Dispose();
            _weather = null;
        }

        if (_stage is null) return;

        _renderer.RemoveEntity(_stage.Map.Foreground);
        _renderer.RemoveEntity(_stage.Map);
        _renderer.ClearLights();
        _renderer.Occlusion = null;

        Discard(_stage);
        _stage = null;
    }
}
