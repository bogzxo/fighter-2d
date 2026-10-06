using System.Numerics;

using Horizon.Engine;
using Horizon.Rendering;
using Horizon.Rendering.PostProcessing;
using Horizon.Rendering.UIX;

namespace Fighter2D;

/// <summary>
/// The post processing every scene shares, so the menus and the fights all look like they are on the same crusty old CRT.
/// Scenes draw into a renderer made here instead of straight onto the window. UI is laid on top of that and stays sharp.
/// The switches in here are what an options screen would flip, once there is one.
/// </summary>
internal static class Screen
{
    /// <summary>
    /// Whether the game is drawn with the CRT effect.
    /// </summary>
    public static bool CrtEnabled = true;

    /// <summary>
    /// Whether moving things get motion blur. In a fight it hides that pixel art moves in steps, in the menus it smooths the slides and pops.
    /// </summary>
    public static bool MotionBlurEnabled = true;

    // The art is drawn at twice its size, so that is how big one dot of the CRT is
    private const float CRT_PIXEL_SIZE = 2.0f;

    // Kept gentle, the menu buttons are laid over the screen and have to stay lined up with what is drawn behind them
    private static readonly Vector2 CrtWarp = new(1.0f / 64.0f, 1.0f / 48.0f);

    /// <summary>
    /// Helper method to give a scene a renderer with the CRT effect on it.
    /// </summary>
    public static Renderer2D CreateRenderer(Scene scene)
    {
        Vector2 size = GameEngine.Instance.WindowManager.ViewportSize;

        var renderer = scene.AddEntity(new Renderer2D((uint)size.X, (uint)size.Y));
        AddCrt(renderer);

        return renderer;
    }

    /// <summary>
    /// Helper method to put the CRT effect on a renderer. If the renderer gets motion blur too, add that first.
    /// </summary>
    public static void AddCrt(Renderer2D renderer)
    {
        renderer.PostProcessing.Add(new CrtEffect
        {
            Enabled = CrtEnabled,
            PixelSize = CRT_PIXEL_SIZE,
            Warp = CrtWarp
        });
    }

    /// <summary>
    /// Helper method to put motion blur on the world of a fight.
    /// </summary>
    public static void AddMotionBlur(DeferredRenderer2D world)
    {
        world.PostProcessing.Add(new MotionBlurEffect { Enabled = MotionBlurEnabled });
    }

    /// <summary>
    /// Helper method to put motion blur on a UI. Only the UI gets blurred, whatever is behind it is left alone.
    /// </summary>
    public static void AddMotionBlur(UICompositor ui)
    {
        ui.PostProcessing.Add(new MotionBlurEffect { Enabled = MotionBlurEnabled });
    }
}
