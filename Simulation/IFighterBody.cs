using System.Numerics;

namespace Fighter2D.Simulation;

/// <summary>
/// A fighter's presence in the world as the simulation sees it, where they are, how fast they are going, whether their
/// feet are on something, which way they face, which frame they show and the boxes of that frame. <see cref="Player"/>
/// is one of these over its sprite and its physics body, a test's fake is one over a handful of numbers, and nothing
/// under <see cref="PlayerController"/> knows the difference. That is what keeps the engine out of the simulation.
/// </summary>
internal interface IFighterBody
{
    /// <summary>The middle of the fighter, what everything else is measured from.</summary>
    Vector2 Position { get; }

    /// <summary>Where the fighter touches the ground, this is where dust comes from.</summary>
    Vector2 FeetPosition { get; }

    /// <summary>Where the head is, this is where the haze of a hitstun hangs.</summary>
    Vector2 HeadPosition { get; }

    /// <summary>How big the fighter is against the art it was drawn at.</summary>
    float Scale { get; }

    Vector2 Velocity { get; }
    void SetVelocity(Vector2 velocity);
    void ApplyImpulse(Vector2 impulse);

    /// <summary>Moves the fighter a little without a word to anybody, the network easing a player over to where they belong.</summary>
    void Nudge(Vector2 step);

    /// <summary>Puts the fighter somewhere else outright, not shown on the way there.</summary>
    void Teleport(Vector2 position, Vector2 velocity);

    /// <summary>How much of the world's gravity pulls on the fighter, and the fastest they fall.</summary>
    float GravityScale { get; set; }
    float MaxFallSpeed { get; set; }

    /// <summary>How hard the world pulls, which a character's own gravity is a share of. Zero before there is a world.</summary>
    float WorldGravity { get; }

    /// <summary>Whether the feet are on something.</summary>
    bool IsGrounded { get; }

    /// <summary>Whether the fighter faces left. <see cref="Facing"/> is the same thing as a direction.</summary>
    bool Flipped { get; set; }
    float Facing => Flipped ? -1.0f : 1.0f;

    /// <summary>Whether the other fighter is in front of us rather than behind our back. Somebody right on top of us is both.</summary>
    bool IsFacing(IFighterBody other)
    {
        float toOther = other.Position.X - Position.X;
        return toOther == 0.0f || (toOther < 0.0f) == Flipped;
    }

    /// <summary>Puts the fighter on an animation. One the character doesn't have leaves them on what they were showing.</summary>
    void PlayAnimation(string animation);

    /// <summary>The animation the fighter is on, which is the one last asked for unless the character hasn't got it.</summary>
    string? ShownAnimation { get; }

    /// <summary>Shows a frame of the animation the fighter is on.</summary>
    void ShowFrame(int frame);

    /// <summary>How many frames an animation has, 0 for one the character doesn't have.</summary>
    int FrameCount(string animation);

    /// <summary>Paints the fighter over in a colour for a moment, the flash of a hit.</summary>
    void Flash(Vector4 colour, float time, float amount);

    /// <summary>Tints the fighter over a while, a knocked out one dimming.</summary>
    void TintTo(Vector4 tint, float time);

    /// <summary>The box round everything of the fighter that is drawn, where it is in the world, what gets hit.</summary>
    Box Hurtbox { get; }

    /// <summary>The box of whatever limb sticks out on a frame, where it is in the world, what hits.</summary>
    Box Hitbox(uint frame);

    /// <summary>Whether our hitbox on a frame touches their hurtbox, the way the physics tests it.</summary>
    bool HitboxTouches(uint frame, IFighterBody other);
}
