using System.IO;
using System.Numerics;

using Horizon.Core;
using Horizon.Engine;
using Horizon.Rendering.UIX;

namespace Fighter2D.Scenes;

/// <summary>
/// Where the menus get their UI from: every one of them is a layout file in Assets/ui/layouts, drawn up with Horizon.Hex.
/// The scenes look the parts of their layout up by name and fill in whatever is not known until the game runs.
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
    public const string FIGHTSCENE_HUD = "fight_overlay.hor";

    private const string DIRECTORY = "Assets/ui/layouts";

    // The size of the screen the layouts were made for, the UI scales itself to whatever the window is
    public static readonly Vector2 DesignSize = new(1600, 900);

    /// <summary>
    /// Helper method to give a scene its UI out of a layout file.
    /// </summary>
    /// <param name="scene">The scene the UI is for, it gets the compositor. The scene itself and not its glass (see Screen):
    /// the menus are laid over the picture of the tube rather than shown on it, sharp and where the mouse takes them to be.</param>
    /// <param name="file">The name of the layout, one of the constants of this class.</param>
    public static UILayout Load(Entity scene, Camera2D camera, string file)
    {
        var compositor = scene.AddComponent(new UICompositor(camera, Constants.UI_THEME) { DesignSize = DesignSize });

        // The menus slide and pop into place, which is smeared the way a camera would have caught it
        Screen.Blur(compositor);

        return compositor.CreateModule().LoadLayout(Path.Combine(DIRECTORY, file));
    }
    /// <summary>
    /// Helper method to give a scene its UI out of a layout file.
    /// </summary>
    public static (UILayout, UICompositor) Load(Camera2D camera, string file)
    {
        var compositor = new UICompositor(camera, Constants.UI_THEME) { DesignSize = DesignSize };

        // The menus slide and pop into place, which is smeared the way a camera would have caught it
        Screen.Blur(compositor);

        var layout = compositor.CreateModule().LoadLayout(Path.Combine(DIRECTORY, file));

        return (layout, compositor);
    }
}
