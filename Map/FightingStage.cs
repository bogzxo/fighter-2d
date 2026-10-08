using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.Logging;

using Horizon.Physics;
using Horizon.Rendering;
using Horizon.Rendering.Lighting;
using Horizon.Rendering.Tiling;

namespace Fighter2D.Map;

/// <summary>
/// The stage a fight happens on. Loads the tile map and turns it into everything a fight needs from it.
/// That is the collision, the shadows, where the players spawn, and the emitters and lights somebody placed in Tiled.
/// </summary>
internal sealed class FightingStage : IDisposable
{
    // How far apart the players start on a map that only says where the first one does
    private const float SPAWN_GAP = 256;

    // How big a light of the map is unless it says so itself (light_size), big enough for soft shadow edges
    private const float DEFAULT_LIGHT_SIZE = 5.0f;

    // How far in front of the map its lights hover unless they say so themselves (light_height).
    // The normal maps of the tiles go by this, the lower a light the more it rakes across them
    private const float DEFAULT_LIGHT_HEIGHT = 40.0f;

    public TileMap Map { get; }

    /// <summary>
    /// Where the two players start.
    /// </summary>
    public Vector2 LeftSpawn { get; }
    public Vector2 RightSpawn { get; }

    /// <summary>
    /// The corners of the box around every tile of the map, the camera is kept inside of it.
    /// </summary>
    public Vector2 Min { get; private set; } = new(float.MaxValue);
    public Vector2 Max { get; private set; } = new(float.MinValue);

    /// <summary>
    /// Whether the map has any tiles at all, an empty map has nothing to keep the camera in.
    /// </summary>
    public bool HasBounds => Min.X <= Max.X;

    /// <summary>
    /// Finds the way from one spot of the stage to another around its solid tiles, for anybody who walks somewhere without
    /// a player steering them (see StageRoute). Null for a stage nobody stands on.
    /// </summary>
    public TileMapPathfinder? Paths { get; }

    private readonly OcclusionMap2D _occlusion;

    // The stuff the map asked for, kept until there is somebody to hand it to
    private readonly List<(string Kind, Vector2 Position, float Rate)> _emitters = [];
    private readonly List<Light2D> _lights = [];

    // The lights that move by themselves, a lantern swinging in the wind or a beacon going round, and how
    private readonly List<AnimatedLight> _animated = [];
    private float _time;

    /// <summary>
    /// A light of the map that turns by itself. It swings either side of the way it was placed (light_sway, in
    /// degrees, light_sway_rate times a second) and spins (light_spin, degrees a second), both at once if asked.
    /// Every one is a little out of step with the others, a row of lanterns swinging in time would look like a machine.
    /// </summary>
    private sealed record AnimatedLight(Light2D Light, float Rest, float Sway, float SwayRate, float Spin, float Phase);
    private readonly Vector2?[] _spawns = new Vector2?[2];

    /// <param name="world">The physics world the collision of the map goes into, null for a stage nobody is going to stand on (the map preview).</param>
    public FightingStage(MapDefinition definition, DeferredRenderer2D renderer, PhysicsWorld? world)
    {
        string path = GameContent.PathOf(GameContent.MAPS_DIRECTORY + "/" + definition.FileName);

        if (!TileMap.TryLoad(path, out var map) || map is null)
        {
            throw new Exception("Failed to load map file... fucked up Tiled XML or missing file.");
        }

        Map = map;

        // The middle of the bottom left tile sits at zero, spawn_pos of maps.hor counts on that
        Map.Origin = -Map.TileSize / 2;
        renderer.AddEntity(Map);

        // Everything that was placed in the map rather than painted, sorted by what it is.
        // The templates in Assets/maps/objects put these together in Tiled, an object with the same properties and no template works too
        Map.DispatchObjects(objects => objects
            .OfClass("spawn", ReadSpawn)
            .WithProperty("emitter_type", ReadEmitter)
            .WithProperty("light_radius", ReadLight));

        if (world is not null)
        {
            BuildColliders(world);
            Paths = Map.CreatePathfinder();
        }
        renderer.Occlusion = _occlusion = BuildOcclusion();
        MeasureBounds();

        // The map has the last word on where the players start, maps.hor is for the maps that don't say
        LeftSpawn = _spawns[0] ?? new Vector2(
            definition.SpawnPosition.X * Map.TileSize.X,
            (MapLoader.SPAWN_BASE_ROW - definition.SpawnPosition.Y) * Map.TileSize.Y);
        RightSpawn = _spawns[1] ?? LeftSpawn + new Vector2(SPAWN_GAP, 0);

        // The parallax layers line up the way they were drawn when the camera is between the two spawns
        Map.ParallaxOrigin = (LeftSpawn + RightSpawn) / 2.0f;
    }

    /// <summary>
    /// Helper method to build the static collision of the map, tiles next to each other come as one box.
    /// </summary>
    private void BuildColliders(PhysicsWorld world)
    {
        PhysicsBodyComponent2D body = world.CreateBody(PhysicsBodySimulationType.Static);

        foreach (TileMapBox box in Map.BuildColliders())
        {
            body.CreateRectangularFixture(box.Min, box.Size);
        }
    }

    /// <summary>
    /// Helper method to mark the tiles that cast shadows. Which ones do is up to the layers of the map (CastsShadows).
    /// </summary>
    private OcclusionMap2D BuildOcclusion()
    {
        var occlusion = new OcclusionMap2D(Map.Width, Map.Height, Map.Origin, Map.TileSize);

        foreach (Vector2 solid in Map.ShadowCasters())
        {
            occlusion.Set(solid, true);
        }

        return occlusion;
    }

    private void MeasureBounds()
    {
        // Every tile counts, solid or not
        foreach (TileMapCell cell in Map.Tiles())
        {
            Min = Vector2.Min(Min, cell.Centre - Map.TileSize / 2);
            Max = Vector2.Max(Max, cell.Centre + Map.TileSize / 2);
        }
    }

    /// <summary>
    /// Called for every spawn object of the map, which says where a player starts and which one (player: 1 or 2).
    /// </summary>
    private void ReadSpawn(TileMapObject spawn)
    {
        int player = spawn.Properties.GetInt("player", 1);
        if (player < 1 || player > _spawns.Length)
        {
            Log.Warning($"Spawn '{spawn.Name}' is for player {player}, there are only {_spawns.Length}!");
            return;
        }

        _spawns[player - 1] = spawn.Position;
    }

    /// <summary>
    /// Called for every object of the map that has an emitter_type, those leak particles (water, lava).
    /// </summary>
    private void ReadEmitter(TileMapObject emitter)
    {
        if (!emitter.Properties.TryGetFloat("emitter_rate", out float rate))
        {
            Log.Warning($"Emitter '{emitter.Name}' has no emitter_rate, or one that isn't a number!");
            return;
        }

        _emitters.Add((emitter.Properties.GetString("emitter_type"), emitter.Position, rate));
    }

    /// <summary>
    /// Called for every object of the map that has a light_radius. Anything with a radius is a light, the rest is optional.
    /// light_type is "point" (the usual), "spot" (shines in a cone, light_angle in degrees with 0 along the ground to
    /// the right and 90 straight up, light_cone the width of the cone in degrees, light_cone_softness how much of
    /// its edge fades) or "directional" (the moon, the same everywhere, light_angle the way its rays go,
    /// light_reach how far it looks for shadows). light_sprite_shadow is how much of the light the fighters take when
    /// they stand in its way, 1 for all of it. light_sway, light_sway_rate and light_spin set it moving, see AnimatedLight.
    /// </summary>
    private void ReadLight(TileMapObject light)
    {
        float radius = light.Properties.GetFloat("light_radius");
        if (radius <= 0.0f)
        {
            Log.Warning($"Light '{light.Name}' has a light_radius that isn't a number above zero!");
            return;
        }

        Vector4 colour = light.Properties.GetColor("light_colour", Vector4.One);
        LightType type = light.Properties.GetString("light_type", "point").ToLowerInvariant() switch
        {
            "spot" => LightType.Spot,
            "directional" => LightType.Directional,
            _ => LightType.Point
        };

        // Tiled's y goes down the screen, the world's goes up, so the angle is read the way a person thinks of it
        // (up is up) and turned into the engine's
        float angle = light.Properties.GetFloat("light_angle", -90.0f) * MathF.PI / 180.0f;

        var made = new Light2D
        {
            Type = type,
            Position = light.Position,
            Radius = radius,
            Color = new Vector3(colour.X, colour.Y, colour.Z),
            Intensity = light.Properties.GetFloat("light_intensity", 1.0f),
            Glow = light.Properties.GetFloat("light_glow"),
            Flicker = light.Properties.GetFloat("light_flicker"),
            Size = light.Properties.GetFloat("light_size", DEFAULT_LIGHT_SIZE),
            Height = light.Properties.GetFloat("light_height", DEFAULT_LIGHT_HEIGHT),
            CastsShadows = light.Properties.GetBool("light_shadows", true),
            SpriteShadow = light.Properties.GetFloat("light_sprite_shadow", 1.0f),
            Direction = angle,
            ConeAngle = light.Properties.GetFloat("light_cone", 90.0f) * MathF.PI / 180.0f,
            ConeSoftness = light.Properties.GetFloat("light_cone_softness", 0.5f),
            Reach = light.Properties.GetFloat("light_reach", radius)
        };
        _lights.Add(made);

        float sway = light.Properties.GetFloat("light_sway");
        float spin = light.Properties.GetFloat("light_spin");
        if (sway != 0.0f || spin != 0.0f)
        {
            _animated.Add(new AnimatedLight(
                made,
                angle,
                sway * MathF.PI / 180.0f,
                light.Properties.GetFloat("light_sway_rate", 0.5f),
                spin * MathF.PI / 180.0f,
                _animated.Count * 1.7f));
        }
    }

    /// <summary>
    /// Moves the lights that move by themselves on a step. Every update of the fight, with the fight's clock, so
    /// they hang still when the fight is paused.
    /// </summary>
    public void Tick(float dt)
    {
        if (_animated.Count == 0) return;

        _time += dt;
        foreach (AnimatedLight light in _animated)
        {
            float swing = light.Sway * MathF.Sin((_time * light.SwayRate + light.Phase) * MathF.Tau);
            light.Light.Direction = light.Rest + swing + light.Spin * _time;
        }
    }

    /// <summary>
    /// Hands the emitters of the map over to the effects, which run them from then on.
    /// </summary>
    public void SpawnEmitters(FightEffects effects)
    {
        foreach (var (kind, position, rate) in _emitters)
        {
            if (!effects.AddEmitter(kind, position, rate))
            {
                Log.Warning($"Skipped a '{kind}' emitter, there is no such preset or its rate isn't above zero!");
            }
        }

        _emitters.Clear();
    }

    /// <summary>
    /// Hands the lights of the map over to the renderer.
    /// </summary>
    public void SpawnLights(DeferredRenderer2D renderer)
    {
        foreach (Light2D light in _lights)
        {
            renderer.AddLight(light);
        }

        _lights.Clear();
    }

    public void Dispose() => _occlusion.Dispose();
}
