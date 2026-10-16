using Horizon.Content;

namespace Fighter2D.App;

internal static class Constants
{
    public const string VERSION_LABEL = "v0.3 Beta (Pre-Content)";
    public const string LOCALISATION_DIRECTORY = "Assets/ui/localisation";

    // What the window says, in whichever language is loaded, so it has to be asked for after that
    public static string WindowTitle => Localisation.Format("app.window_title", ("version", VERSION_LABEL));
    public const string UI_SKIN = "ink";
    public const string UI_THEME = "night";
}
