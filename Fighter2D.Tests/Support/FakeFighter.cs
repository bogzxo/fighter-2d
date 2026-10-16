using System.Numerics;

using Fighter2D.Data;
using Fighter2D.Fighters;

namespace Fighter2D.Tests.Support;

/// <summary>
/// A fighter with no sprite and no physics world, a position, a velocity and a floor at the feet. It falls under the
/// gravity the controller gives it, stops on the floor, and its boxes are a body sized box round it and a limb sized
/// one in front, which is all a hit needs to land. The frames an animation has come from the character's own art, so
/// the moves last exactly as long as they do in the game.
/// </summary>
internal sealed class FakeFighter : IFighter, IFighterBody
{
    // The same shape as a fighter in the game, about 30 wide and 73 tall, the feet 64 under the middle
    private const float WIDTH = 30.0f, HEIGHT = 73.0f, FEET = 64.0f;

    // How far a limb reaches in front of the body on a frame with a hit on it
    private const float REACH = 50.0f;

    private readonly Func<string, int> _frames;
    private Vector2 _position;

    public FakeFighter(CharacterDefinition character, MoveList moves, Func<string, int> frames, float x, bool facesLeft)
    {
        Character = character;
        MoveList = moves;
        _frames = frames;
        _position = new Vector2(x, FEET);
        Flipped = facesLeft;
    }

    /* IFighter */

    public CharacterDefinition Character { get; }
    public MoveList MoveList { get; }
    public PlayerController Controller { get; set; } = null!;
    public FightContext Fight { get; set; } = null!;
    public IFighterBody Body => this;
    public byte Health { get; set; } = CombatRules.MAX_HEALTH;
    public byte Meter { get; set; }

    /* IFighterBody */

    public Vector2 Position => _position;
    public Vector2 FeetPosition => _position - new Vector2(0.0f, FEET);
    public Vector2 HeadPosition => _position + new Vector2(0.0f, 2.0f);
    public float Scale => 1.0f;
    public Vector2 Velocity { get; private set; }
    public void SetVelocity(Vector2 velocity) => Velocity = velocity;
    public void ApplyImpulse(Vector2 impulse) => Velocity += impulse;
    public void Nudge(Vector2 step) => _position += step;

    public void Teleport(Vector2 position, Vector2 velocity)
    {
        _position = position;
        Velocity = velocity;
    }

    public float GravityScale { get; set; } = 1.0f;
    public float MaxFallSpeed { get; set; } = float.MaxValue;
    public float WorldGravity => 3000.0f;
    public bool IsGrounded => FeetPosition.Y <= 0.0f && Velocity.Y <= 0.0f;
    public bool Flipped { get; set; }
    public float Facing => Flipped ? -1.0f : 1.0f;

    public string? ShownAnimation { get; private set; }
    public int Frame { get; private set; }
    public int FrameCount(string animation) => _frames(animation);

    public void PlayAnimation(string animation)
    {
        if (FrameCount(animation) > 0) ShownAnimation = animation;
    }

    public void ShowFrame(int frame) => Frame = frame;
    public void Flash(Vector4 colour, float time, float amount) { }
    public void TintTo(Vector4 tint, float time) { }

    public Box Hurtbox => new(FeetPosition + new Vector2(-WIDTH / 2, 0.0f), FeetPosition + new Vector2(WIDTH / 2, HEIGHT));

    public Box Hitbox(uint frame)
    {
        float near = _position.X + Facing * (WIDTH / 2), far = _position.X + Facing * (WIDTH / 2 + REACH);
        return new Box(new Vector2(MathF.Min(near, far), FeetPosition.Y + 10.0f), new Vector2(MathF.Max(near, far), FeetPosition.Y + HEIGHT - 10.0f));
    }

    public bool HitboxTouches(uint frame, IFighterBody other) => Hitbox(frame).Overlaps(other.Hurtbox, out _);

    /// <summary>
    /// Moves the body on by a step of time, the way the physics world would. Falls, stops on the floor.
    /// </summary>
    public void Integrate(float dt)
    {
        Vector2 velocity = Velocity;
        velocity.Y -= WorldGravity * GravityScale * dt;
        if (velocity.Y < -MaxFallSpeed) velocity.Y = -MaxFallSpeed;

        _position += velocity * dt;

        if (FeetPosition.Y <= 0.0f)
        {
            _position.Y = FEET;
            if (velocity.Y < 0.0f) velocity.Y = 0.0f;
        }

        Velocity = velocity;
    }
}
