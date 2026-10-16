using System;
using System.Numerics;

using Horizon.Rendering;
using Horizon.Rendering.Lighting;

namespace Fighter2D.Effects;

/// <summary>
/// A little light that follows each player around, so they can still be made out on a map that is dark as fuck.
/// </summary>
internal sealed class PlayerLights
{
    // Where the light sits against the middle of the player
    private static readonly Vector2 OFFSET = new(0, -28);

    private readonly Light2D[] _lights = new Light2D[2];

    /// <param name="ambient">The ambient light of the map. The darker the map the more the players need a light of their own.</param>
    public PlayerLights(DeferredRenderer2D renderer, Vector3 ambient)
    {
        // In broad daylight they need none at all
        float brightness = (ambient.X + ambient.Y + ambient.Z) / 3.0f;
        float fill = Math.Clamp(0.9f - brightness, 0.0f, 0.5f);

        for (int i = 0; i < _lights.Length; i++)
        {
            // Only there to fill in, so it is weak, wide and shines through everything
            _lights[i] = renderer.AddLight(new Light2D
            {
                Color = new Vector3(0.85f, 0.9f, 1.0f),
                Radius = 120.0f,
                Intensity = fill,
                CastsShadows = false
            });
        }
    }

    public void Follow(Player? playerOne, Player? playerTwo)
    {
        if (playerOne is not null) _lights[0].Position = playerOne.Transform.Position + OFFSET;
        if (playerTwo is not null) _lights[1].Position = playerTwo.Transform.Position + OFFSET;
    }
}
