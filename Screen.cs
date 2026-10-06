using System.Numerics;

using Horizon.Engine;
using Horizon.Rendering;
using Horizon.Rendering.PostProcessing;
using Horizon.Rendering.UIX;

namespace Fighter2D;

/// <summary>
/// The glass the whole game is seen through: every scene puts what it shows into a renderer made here rather than
/// straight onto the window, which is what has the backdrops of the menus and the fights look like they are on the
/// same old screen. What is UI is not behind it but laid over it: the buttons of the menus (see MenuLayouts) and the HUD.
/// What the options will one day switch on and off is here as well.
/// </summary>
internal static class Screen
{
    /// <summary>
    /// Whether the game looks like it is shown on a picture tube.
    /// </summary>
    public static bool Tube = true;

    /// <summary>
    /// Whether what moves is blurred along the way it moves: in a fight that hides that pixel art moves in steps,
    /// in the menus it is what slides and pops into place.
    /// </summary>
    public static bool MotionBlur = true;

    // The art is drawn at twice its size, so that is how big a dot of the tube is
    private const float TUBE_PIXEL_SIZE = 2.0f;

    // Gentle, the menus are laid over the glass and their buttons have to stay near what is drawn behind them
    private static readonly Vector2 TubeWarp = new(1.0f / 64.0f, 1.0f / 48.0f);

    public static Renderer2D For(Scene scene)
    {
        Vector2 size = GameEngine.Instance.WindowManager.ViewportSize;

        var screen = scene.AddEntity(new Renderer2D((uint)size.X, (uint)size.Y));
        Glass(screen);

        return screen;
    }

    /// <summary>
    /// Helper method to put a renderer behind the glass by itself, for a scene that shows nothing else behind it:
    /// that saves drawing its picture into a glass of its own first. Goes after <see cref="Blur(DeferredRenderer2D)"/>.
    /// </summary>
    public static void Glass(Renderer2D renderer)
    {
        renderer.PostProcessing.Add(new CrtEffect
        {
            Enabled = Tube,
            PixelSize = TUBE_PIXEL_SIZE,
            Warp = TubeWarp
        });
    }

    /// <summary>
    /// Helper method to blur what moves in a world, see <see cref="MotionBlur"/>.
    /// </summary>
    public static void Blur(DeferredRenderer2D world)
    {
        world.PostProcessing.Add(new MotionBlurEffect { Enabled = MotionBlur });
    }

    /// <summary>
    /// Helper method to blur what moves in a UI, see <see cref="MotionBlur"/>. Only the UI is, whatever is behind it is left alone.
    /// </summary>
    public static void Blur(UICompositor ui)
    {
        ui.PostProcessing.Add(new MotionBlurEffect { Enabled = MotionBlur });
    }
}
