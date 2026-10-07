using System;
using System.Numerics;

using Fighter2D.Character;

namespace Fighter2D.Combat;

/// <summary>
/// Keeps the two fighters from standing in (or on) each other. Their bodies don't collide, they are in the same collision group
/// and pass straight through one another. What keeps them apart is this, a shove sideways for as long as their pushboxes overlap.
/// So a jump kick that comes down on somebody slides off to the side of them instead of landing on their head and getting stuck there.
/// </summary>
internal static class Pushboxes
{
    // The pushbox of a fighter, in pixels of their art (it grows with the scale of the character).
    // As wide as the two of them are allowed to get into each other, and about as tall as they stand
    private const float WIDTH = 30.0f;
    private const float HEIGHT = 52.0f;

    // How hard they are shoved apart when one is right inside of the other, less the less they overlap.
    // Enough to win against somebody walking into it, not so much that anybody gets flung
    private const float PUSH = 1400.0f;

    /// <summary>
    /// Called once per physics step with both fighters, shoves them apart if their pushboxes overlap.
    /// </summary>
    public static void Separate(Player one, Player two, float dt)
    {
        if (one.PhysicsBody is not { } first || two.PhysicsBody is not { } second) return;

        // Somebody who is down for good is furniture, nobody trips over them
        if (one.Health == 0 || two.Health == 0) return;

        float width = WIDTH * (one.Scale + two.Scale) * 0.5f;
        float height = HEIGHT * (one.Scale + two.Scale) * 0.5f;

        Vector2 apart = second.Position - first.Position;
        if (MathF.Abs(apart.Y) >= height) return;

        float overlap = width - MathF.Abs(apart.X);
        if (overlap <= 0.0f) return;

        // Right on top of each other there is no way that is away, so player one goes left by decree
        float direction = apart.X != 0.0f ? MathF.Sign(apart.X) : 1.0f;
        float shove = PUSH * (overlap / width) * dt;

        first.ApplyImpulse(new Vector2(-direction * shove, 0.0f));
        second.ApplyImpulse(new Vector2(direction * shove, 0.0f));
    }
}
