using System;
using System.Collections.Generic;

using Horizon.HIDL.Runtime;

namespace Fighter2D.Fighters;

/// <summary>
/// How a character gets about, the `movement` of its entry in characters.hor. Every number is what the character
/// actually does, in world units and seconds (a fighter is about 30 wide and 73 tall, a tile 16), there is no drag
/// in between eating some of it. The body used to have a drag of 16 on it, a walk speed of 2000 came out at about
/// 430 and a jump needed 3500 to get anywhere, which is why none of the old numbers meant anything.
/// </summary>
internal sealed record CharacterMovement
{
    // How much of the old walk_speed a character actually walked at against the drag, for a character that hasn't got a movement of its own yet
    private const float OLD_WALK_SHARE = 0.213f;

    // The speed they walk at, how quickly they get up to it and how quickly they stop when nothing is held (or a move plants them)
    public float WalkSpeed { get; init; } = 420.0f;
    public float GroundAcceleration { get; init; } = 4000.0f;
    public float Traction { get; init; } = 2800.0f;

    // The same in the air. Drifting is slower than walking, and letting go in the air keeps most of the speed, the Smash way
    public float AirSpeed { get; init; } = 330.0f;
    public float AirAcceleration { get; init; } = 1600.0f;
    public float AirFriction { get; init; } = 500.0f;

    // How hard they fall, units a second every second, how fast they fall at most, and how fast with down pressed past the top of a jump
    public float Gravity { get; init; } = 3000.0f;
    public float FallSpeed { get; init; } = 620.0f;
    public float FastFallSpeed { get; init; } = 950.0f;

    // How high the jumps go, from the feet. A tap of up is a short hop, held through the squat a full one
    public float JumpHeight { get; init; } = 134.0f;
    public float ShortHopHeight { get; init; } = 60.0f;
    public int DoubleJumps { get; init; } = 1;
    public float DoubleJumpHeight { get; init; } = 100.0f;

    // How long a step of the physics is. The world speeds a body up before it moves it, which loses half a step of
    // speed on the way up, so everything that has to get somewhere exactly is sent off that much quicker
    private const float PHYSICS_STEP = 1.0f / 120.0f;

    // How much of their gravity they fall with while they're in hitstun, which is what lets them be hit again on the way down.
    // Rivals of Aether has this at about half (its hitstun_grav), a move can say otherwise for its own hits (hitstun_gravity)
    public float HitstunGravity { get; init; } = 0.5f;

    // How heavy they are, 100 the usual. A move's launch_growth sends somebody heavier that much less further, 200 half as far
    public float Weight { get; init; } = 100.0f;

    /// <summary>
    /// How fast something has to leave the ground to get so high, or a jump to go that high.
    /// </summary>
    public float SpeedToReach(float height, float gravityShare = 1.0f)
    {
        if (height <= 0.0f) return 0.0f;

        float gravity = Gravity * gravityShare;
        return MathF.Sqrt(2.0f * gravity * height) + gravity * PHYSICS_STEP * 0.5f;
    }

    /// <summary>
    /// How fast something has to be going along the ground to slide so far before the traction stops it.
    /// </summary>
    public float SpeedToSlide(float distance) => MathF.Sqrt(2.0f * Traction * MathF.Max(0.0f, distance));

    /// <summary>
    /// How long a jump of so high spends in the air, up and down again to where it left from, in seconds.
    /// </summary>
    public float AirTime(float height)
    {
        float up = SpeedToReach(height) / Gravity;

        // The way down tops out at the fall speed
        float reachFallSpeed = FallSpeed / Gravity;
        float freeFall = 0.5f * Gravity * reachFallSpeed * reachFallSpeed;
        float down = height <= freeFall ? MathF.Sqrt(2.0f * height / Gravity) : reachFallSpeed + (height - freeFall) / FallSpeed;
        return up + down;
    }

    /// <summary>
    /// Helper method to read the movement of a character. One that hasn't got one gets the defaults, walking at what its
    /// old walk_speed used to come out at, and gets told about it.
    /// </summary>
    public static CharacterMovement Read(Dictionary<string, IRuntimeValue> character, out string? warning)
    {
        warning = null;
        var fallback = new CharacterMovement();

        if (!HorReader.TryObject(character, "movement", out var movement))
        {
            if (!character.ContainsKey("walk_speed")) return fallback;

            warning = "it has an old walk_speed and no movement, it walks at what that used to come out at. Give it a movement.";
            return fallback with { WalkSpeed = HorReader.Number(character, "walk_speed", 2000.0f) * OLD_WALK_SHARE };
        }

        HorReader.RejectUnknownKeys(movement, Keys, "a movement");

        float Read(string key, float fallbackValue) => MathF.Max(0.0f, HorReader.Number(movement, key, fallbackValue));

        return new CharacterMovement
        {
            WalkSpeed = Read("walk_speed", fallback.WalkSpeed),
            GroundAcceleration = Read("ground_acceleration", fallback.GroundAcceleration),
            Traction = Read("traction", fallback.Traction),
            AirSpeed = Read("air_speed", fallback.AirSpeed),
            AirAcceleration = Read("air_acceleration", fallback.AirAcceleration),
            AirFriction = Read("air_friction", fallback.AirFriction),
            Gravity = MathF.Max(1.0f, Read("gravity", fallback.Gravity)),
            FallSpeed = Read("fall_speed", fallback.FallSpeed),
            FastFallSpeed = Read("fast_fall_speed", fallback.FastFallSpeed),
            JumpHeight = Read("jump_height", fallback.JumpHeight),
            ShortHopHeight = Read("short_hop_height", fallback.ShortHopHeight),
            DoubleJumps = (int)Read("double_jumps", fallback.DoubleJumps),
            DoubleJumpHeight = Read("double_jump_height", fallback.DoubleJumpHeight),
            HitstunGravity = Math.Clamp(Read("hitstun_gravity", fallback.HitstunGravity), 0.05f, 2.0f),
            Weight = MathF.Max(1.0f, Read("weight", fallback.Weight))
        };
    }

    private static readonly HashSet<string> Keys =
    [
        "walk_speed", "ground_acceleration", "traction", "air_speed", "air_acceleration", "air_friction", "gravity", "fall_speed",
        "fast_fall_speed", "jump_height", "short_hop_height", "double_jumps", "double_jump_height", "hitstun_gravity", "weight"
    ];
}
