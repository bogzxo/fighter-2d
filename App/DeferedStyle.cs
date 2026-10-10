using Horizon.Rendering;
using Horizon.Rendering.Tiling;

namespace Fighter2D.App;

/// <summary>
/// How the world of a fight is shaded, every number of it that is a matter of taste, in one place so the look can
/// be tuned without going looking for where each one is set. What a number does is said next to it, what it is
/// applied to is at the bottom. What a player gets to choose (the CRT, the fancy lighting) is in
/// <see cref="GameOptions"/>, what a map says about itself (its ambient light, its lamps) is in the map.
/// </summary>
internal static class DeferedStyle
{
    /* The lights */

    // The art is one world unit per pixel drawn at twice that, and the lighting follows the art, a pixel of it
    // is lit as a whole. 0 would light every pixel of the screen by itself, smooth, and wrong for pixel art
    public const float LIGHTING_PIXEL_SIZE = 1.0f;

    // How tight the highlights of a shiny surface are, higher is a smaller and sharper glint
    public const float SHININESS = 24.0f;

    // How bright those highlights are, on top of what the specular maps of the tile sets say. 0 for none
    public const float SPECULAR_INTENSITY = 1.0f;

    // How soft the edge of a shadow is, as the size of the lamp throwing it in world units. 0 is a hard edge
    public const float SHADOW_SOFTNESS = 12.0f;

    /* The path traced lighting, what the fancy option turns on */

    // How much of the bounced light is added to the picture
    public const float BOUNCE_STRENGTH = 1.0f;

    // How much of what a surface is hit by it throws on, more and the colours of the arena bleed further
    public const float BOUNCE_CARRY = 0.8f;

    // How much of the map's ambient light is kept when the bounces are on, they do most of its work then
    public const float BOUNCE_AMBIENT = 0.5f;

    /* The ambient occlusion. Three things darken a corner and each has a number of its own */

    // Whether there is any at all
    public const bool OCCLUSION = true;

    // The first, what the artist painted into the material, the _ao maps of the tile sets (the cracks between
    // planks, the window recesses). 0 leaves them out, 1 takes them as painted
    public const float MATERIAL_OCCLUSION = 0.25f;

    // The second, what the tile map is shaped like, the wall behind a floor going darker towards it and round
    // the crates. Whether there is any, how dark it gets right up against the geometry, and how far out it
    // reaches in world units
    public const bool GEOMETRY_OCCLUSION = true;
    public const float GEOMETRY_OCCLUSION_STRENGTH = 1.0f;
    public const float GEOMETRY_OCCLUSION_REACH = 24.0f;

    // The two above together, scaled once more. Leave it at 1 and tune them apart
    public const float BAKED_OCCLUSION = 1.0f;

    // The third, what is worked out every frame from whatever stands where, which is the only one that knows
    // about the fighters (the ground under their feet). How dark it gets (anything over 1 is 1, and 0 skips
    // the work altogether, it is the one of the three that costs anything), how far it looks in world units
    // and in how many directions, more being smoother and costing as much more
    public const float CONTACT_OCCLUSION_STRENGTH = 0.85f;
    public const float CONTACT_OCCLUSION_RADIUS = 16.0f;
    public const int CONTACT_OCCLUSION_SAMPLES = 8;

    // How much of all of the above is taken out of the light of the lamps as well as the ambient light. With
    // none of it a corner right under a lantern shows no occlusion at all, with all of it the lamps look weak
    public const float OCCLUSION_OF_LAMPS = 0.4f;

    /// <summary>
    /// Helper method to give a renderer of the world the look. Which lighting it does is the player's choice, see <see cref="Screen.WorldLighting"/>.
    /// </summary>
    public static void Apply(DeferredRenderer2D renderer)
    {
        renderer.LightingPixelSize = LIGHTING_PIXEL_SIZE;
        renderer.Shininess = SHININESS;
        renderer.SpecularIntensity = SPECULAR_INTENSITY;
        renderer.ShadowSoftness = SHADOW_SOFTNESS;

        renderer.PathTracing.Strength = BOUNCE_STRENGTH;
        renderer.PathTracing.Bounce = BOUNCE_CARRY;
        renderer.PathTracing.AmbientScale = BOUNCE_AMBIENT;
        renderer.PathTracing.MaxCascades = Screen.TracedCascades;

        var occlusion = renderer.AmbientOcclusion;
        occlusion.Enabled = OCCLUSION;
        occlusion.Strength = CONTACT_OCCLUSION_STRENGTH;
        occlusion.Radius = CONTACT_OCCLUSION_RADIUS;
        occlusion.Samples = CONTACT_OCCLUSION_SAMPLES;
        occlusion.BakedStrength = BAKED_OCCLUSION;
        occlusion.DirectStrength = OCCLUSION_OF_LAMPS;
    }

    /// <summary>
    /// Helper method to give the map of a stage its share of the look.
    /// </summary>
    public static void Apply(TileMap map)
    {
        map.OcclusionMapStrength = MATERIAL_OCCLUSION;
        map.GeometryOcclusion = GEOMETRY_OCCLUSION;
        map.GeometryOcclusionStrength = GEOMETRY_OCCLUSION_STRENGTH;
        map.GeometryOcclusionReach = GEOMETRY_OCCLUSION_REACH;
    }
}
