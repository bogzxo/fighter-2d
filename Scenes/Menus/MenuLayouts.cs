using System.IO;
using System.Numerics;

using Horizon.Core;
using Horizon.Engine;
using Horizon.UI;

namespace Fighter2D.Scenes.Menus;

/// <summary>
/// Where the menus get their UI from. Every one of them is a layout file in Assets/ui/layouts, drawn up with Horizon.Hex.
/// The scenes look the parts of their layout up by name and fill in whatever isn't known until the game runs.
/// </summary>
internal static class MenuLayouts
{
    public const string MAIN_MENU = "main_menu.hor";
    public const string GAMEPAD_SELECT = "gamepad_select.hor";
    public const string CHARACTER_SELECT = "character_select.hor";
    public const string MAP_SELECT = "map_select.hor";
    public const string MATCH_RULES = "match_rules.hor";
    public const string BINDINGS = "bindings.hor";
    public const string JOIN_SERVER = "join_server.hor";
    public const string OPTIONS = "options.hor";
    public const string FIGHT_HUD = "fight_overlay.hor";
    public const string PAUSE_MENU = "pause_menu.hor";
    public const string CONTENT_TRANSFER = "content_transfer.hor";

    private const string DIRECTORY = "Assets/ui/layouts";

    // The size of the screen the layouts were made for, the UI scales itself to whatever the window is
    public static readonly Vector2 DesignSize = new(1600, 900);

    /// <summary>
    /// Helper method to give a scene its UI out of a layout file. The scene owns the compositor and updates and draws it by itself.
    /// </summary>
    /// <param name="file">The name of the layout, one of the constants of this class.</param>
    public static UILayout Load(Entity scene, Camera2D camera, string file)
    {
        var compositor = scene.AddComponent(CreateCompositor(camera));

        return compositor.CreateModule().LoadLayout(Path.Combine(DIRECTORY, file));
    }

    /// <summary>
    /// Helper method to load a layout laid over the whole window, for somebody who wants to update and draw the
    /// compositor themselves (the HUD). It has a camera of its own that keeps up with the window.
    /// </summary>
    public static (UILayout, UICompositor) Load(string file)
    {
        var compositor = Configure(UICompositor.ForScreen(Constants.UI_THEME));

        return (compositor.CreateModule().LoadLayout(Path.Combine(DIRECTORY, file)), compositor);
    }

    private static UICompositor CreateCompositor(Camera2D camera) => Configure(new UICompositor(camera, Constants.UI_THEME));

    private static UICompositor Configure(UICompositor compositor)
    {
        // As big as the player likes their UI, on top of fitting the window
        compositor.DesignSize = DesignSize;
        compositor.Scale = GameOptions.GuiScale;

        return compositor;
    }
}
