using System.Numerics;

using Horizon.Engine;
using Horizon.Rendering;
using Horizon.Rendering.Lighting;
using Horizon.Rendering.PostProcessing;
using Horizon.Rendering.Transitions;
using Horizon.UI;

namespace Fighter2D.App;

/// <summary>
/// The look every scene shares, so the menus and the fights all seem to be on the same crusty old CRT.
/// Scenes draw into a renderer made here instead of straight onto the window. UI is laid on top of that and stays sharp.
/// How one scene hands over to the next is decided here as well. All of it is up to the player, see <see cref="GameOptions"/>.
/// </summary>
internal static class Screen
{
    /// <summary>The engine's performance overlay, made once in Program and set from the options screen.</summary>
    public static Horizon.UI.PerformanceOverlay? Performance { get; set; }

    // The art is drawn at twice its size, so that is how big one dot of the CRT is
    private const float CRT_PIXEL_SIZE = 2.0f;

    // Kept gentle, the menu buttons are laid over the screen and have to stay lined up with what is drawn behind them
    private static readonly Vector2 CrtWarp = new(1.0f / 64.0f, 1.0f / 48.0f);

    // The transitions are made once and used over and over, they let go of what they hold between two uses
    private static readonly BlurTransition Blur = new();
    private static readonly FadeTransition Fade = new() { OutTime = 0.14f, InTime = 0.24f };

    // Rot in blocks of two pixels of the art. The menus get a quick one, nobody wants to watch every screen decay on the way to a fight
    private static readonly RotTransition Rot = new() { PixelSize = CRT_PIXEL_SIZE * 2.0f };
    private static readonly RotTransition QuickRot = new() { PixelSize = CRT_PIXEL_SIZE * 2.0f, OutTime = 0.2f, InTime = 0.26f };

    /// <summary>
    /// How one menu hands over to the next, null for a hard cut.
    /// </summary>
    public static SceneTransition? BetweenMenus => GameOptions.Transitions switch
    {
        TransitionStyle.Off => null,
        TransitionStyle.Fade => Fade,
        TransitionStyle.Rot => QuickRot,
        _ => Blur
    };

    /// <summary>
    /// How the game gets into a fight and back out of it, which is allowed to take its time. Null for a hard cut.
    /// </summary>
    public static SceneTransition? IntoFight => GameOptions.Transitions switch
    {
        TransitionStyle.Off => null,
        TransitionStyle.Fade => Fade,
        TransitionStyle.Blur => Blur,
        _ => Rot
    };

    /// <summary>
    /// Helper method to have every scene change go the way the options say, unless it asks for something else (a fight does).
    /// </summary>
    public static void ApplyTransitions()
    {
        GameEngine.Instance.SceneManager.Transition = BetweenMenus;
    }

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
    /// Helper method to put the CRT effect on a renderer. It has to be the last effect, add the others first.
    /// </summary>
    /// <param name="zoom">The zoom of the camera the renderer is seen through, so the dots of the CRT stay as big as the pixels of the art.</param>
    public static void AddCrt(Renderer2D renderer, float zoom = 1.0f)
    {
        renderer.PostProcessing.Add(new CrtEffect
        {
            Enabled = GameOptions.Crt,
            PixelSize = CRT_PIXEL_SIZE / zoom,
            Warp = CrtWarp
        });
    }

    /// <summary>
    /// How the world of a fight is lit, by what the options say. The fancy one path traces.
    /// </summary>
    public static LightingMode WorldLighting => GameOptions.RenderPathtraced ? LightingMode.PathTraced : LightingMode.Direct;

    /// <summary>
    /// How many cascades the pathtraced lighting works its light out with, by the quality the options say. Every
    /// cascade costs as much as the next, three is about half of all of them, and the light settling over a few
    /// frames is what keeps three looking like the lot. Under three it goes blotchy whatever, see
    /// <see cref="Horizon.Rendering.Lighting.PathTracedLighting2D.MaxCascades"/>.
    /// </summary>
    public static int TracedCascades => GameOptions.LightQuality switch
    {
        LightQuality.Low => 3,
        LightQuality.Medium => 4,
        _ => 6
    };

    /// <summary>
    /// Helper method to flip the effects of a scene that is already up to what the options say, for the options screen itself.
    /// </summary>
    public static void ApplyOptions(Renderer2D renderer, UICompositor? ui)
    {
        if (renderer.PostProcessing.Get<CrtEffect>() is { } crt) crt.Enabled = GameOptions.Crt;
        if (renderer is DeferredRenderer2D lit)
        {
            lit.Lighting = WorldLighting;
            lit.PathTracing.MaxCascades = TracedCascades;
        }
    }
}
