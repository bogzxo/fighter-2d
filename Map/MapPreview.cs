using System.Numerics;

using Fighter2D.Effects;

using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Physics;
using Horizon.Rendering;

namespace Fighter2D.Map;

/// <summary>
/// A live picture of a map, for the map select screen. It is the real thing and not a screenshot, the same tiles, lights and weather a fight on it would have.
/// Nobody is standing on it so it has no collision, and the camera just drifts back and forth over where the fight would start.
/// Ask for a map with <see cref="Show"/> from wherever, it gets loaded the next time the preview is drawn.
/// </summary>
internal sealed class MapPreview : GameObject
{
    // How far (in world units) the camera drifts to either side, and how long (in seconds) one trip there and back takes
    private const float DRIFT = 36.0f;
    private const float DRIFT_TIME = 14.0f;

    private readonly Camera2D _camera;
    private readonly PhysicsWorld _world;
    private readonly Vector2 _viewportSize;
    private readonly Vector2 _focusOffset;
    private readonly TweenContext _tweens = new();

    private DeferredRenderer2D _renderer = null!;

    // The map somebody asked for and the one that is loaded, by their file names
    private MapDefinition _wanted;
    private string? _shownFile;

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
            // The art is one world unit per pixel drawn at twice that, and the lighting follows the art
            LightingPixelSize = 1.0f
        });

        // Back and forth forever, slowly
        _tweens.Play(Tween.To(() => _drift, drift => _drift = drift, 1.0f, DRIFT_TIME / 2).SetEasing(Easing.InOutSine).SetLoops(-1, LoopMode.PingPong));
    }

    /// <summary>
    /// Asks for a map to be shown. Can be called from any thread.
    /// </summary>
    public void Show(MapDefinition map) => _wanted = map;

    public override void UpdateState(float dt)
    {
        _tweens.Tick(dt);
        _view?.LookAt(_focus + new Vector2(_drift * DRIFT, 0.0f));

        base.UpdateState(dt);
    }

    public override void Render(float dt)
    {
        // Loading a map makes a pile of things on the GPU, so it happens here on the render thread and not where it was asked for
        if (_wanted.FileName is { } file && file != _shownFile)
        {
            _shownFile = file;
            Load(_wanted);
        }

        base.Render(dt);
    }

    private void Load(MapDefinition map)
    {
        Unload();

        // The ambient light goes first, the storm of the weather remembers it as what to go back to after a flash
        _renderer.Ambient = map.Lighting.Ambient;

        _stage = new FightingStage(map, _renderer, world: null);
        _stage.SpawnLights(_renderer);

        _weather = _renderer.AddEntity(new Weather(_camera, map, _world, _renderer));
        _renderer.AddEntity(_stage.Map.Foreground);

        _focus = (_stage.LeftSpawn + _stage.RightSpawn) / 2.0f + _focusOffset;
        _view = new FightCamera(_camera, _stage, _viewportSize, _focus);
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

        _stage.Map.Dispose();
        _stage.Dispose();
        _stage = null;
    }
}
