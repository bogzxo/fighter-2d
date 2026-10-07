using System;
using System.Numerics;

using Horizon.Rendering.Particles;

namespace Fighter2D.Effects;

/// <summary>
/// Helper class for throwing a bunch of particles in roughly one direction.
/// </summary>
internal static class ParticleSpray
{
    // The most particles one spray lets go, they live on the stack while they are made
    private const int MAX_COUNT = 128;

    /// <summary>
    /// Helper method to spray particles in a cone, each at its own speed so they don't fly out as one ring.
    /// </summary>
    /// <param name="spread">How wide the cone is, in radians.</param>
    /// <param name="scatter">How far from the position a particle can start, 0 for all of them from the one spot.</param>
    public static void Cone(ParticleRenderer2D renderer, Vector2 position, Vector2 direction, float spread, int count, float minSpeed, float maxSpeed, float scatter = 0.0f)
    {
        float centre = MathF.Atan2(direction.Y, direction.X);
        Span<Particle2D> particles = stackalloc Particle2D[Math.Clamp(count, 0, MAX_COUNT)];

        for (int i = 0; i < particles.Length; i++)
        {
            float angle = centre + (Random.Shared.NextSingle() - 0.5f) * spread;
            var (sin, cos) = MathF.SinCos(angle);

            Vector2 start = position;
            if (scatter > 0.0f)
            {
                start += new Vector2(Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f) * (scatter * 2.0f);
            }

            particles[i] = new Particle2D(new Vector2(cos, sin), start, minSpeed + Random.Shared.NextSingle() * (maxSpeed - minSpeed));
        }

        renderer.AddRange(particles);
    }
}
