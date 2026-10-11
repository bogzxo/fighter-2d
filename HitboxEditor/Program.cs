using System.Globalization;
using System.Numerics;

using Fighter2D.Content;

using Horizon.Core;
using Horizon.Engine;
using Horizon.Logging;

namespace Fighter2D.HitboxEditor;

/// <summary>What the editor was started with.</summary>
internal sealed record EditorOptions(string? Character, string? Move, int? Frame, bool SelfTest, string? Tab = null);

internal static class Program
{
    /// <summary>
    /// The hitbox editor. Loads a character of the game, shows its moves frame by frame on a timeline (startup, active,
    /// recovery, where the hit comes out) with the boxes the game traces off the art, and lets a red hitbox be drawn,
    /// moved, resized and deleted for the hit of a move. Saved into Assets/data/hitboxes/the_character.hor, which the game
    /// loads and uses in place of the traced one.
    /// <code>
    /// dotnet run --project HitboxEditor
    ///   --content ../         the folder with the game's Assets in it, found by itself next to Fighter2D.csproj otherwise
    ///   --character cammy     start on a character, --move sweep on a move of it, --frame 2 on a frame of that
    ///   --tab phases          open on a tab of the inspector, hitbox, move or phases
    ///   --size 1600x900       a window of that size rather than maximised
    ///   --selftest            click through itself, say how it went and go, the files it saves to are put back after
    /// </code>
    /// </summary>
    public static void Main(string[] args)
    {
        // The skin and the fonts of the editor are next to it, wherever it was started from
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        string? content = ValueOf(args, "--content") is { } given ? Path.GetFullPath(given) : FindContent();
        if (content is null)
        {
            Log.Error("[Hitbox editor] Couldn't find the game's content, start it with --content and the folder Assets is in.");
            return;
        }

        GameContent.UseHome(content);

        var options = new EditorOptions(
            ValueOf(args, "--character"),
            ValueOf(args, "--move"),
            int.TryParse(ValueOf(args, "--frame"), CultureInfo.InvariantCulture, out int frame) ? frame : null,
            args.Contains("--selftest"),
            ValueOf(args, "--tab"));

        Vector2? size = ValueOf(args, "--size")?.Split('x') is [var w, var h] && float.TryParse(w, CultureInfo.InvariantCulture, out float width)
            && float.TryParse(h, CultureInfo.InvariantCulture, out float height) ? new Vector2(width, height) : null;

        using var engine = new GameEngine(GameEngineConfiguration.Default with
        {
            WindowConfiguration = WindowManagerConfiguration.Default1600x900 with
            {
                WindowTitle = "Fighter2D hitbox editor",
                WindowSize = size ?? WindowManagerConfiguration.Default1600x900.WindowSize,
                Maximized = size is null && !options.SelfTest
            }
        });

        engine.SetScene(new EditorScene(options));
        engine.Run();
    }

    /// <summary>
    /// Helper method to find the game's own content, the folder with Fighter2D.csproj and Assets/data in it, going up from
    /// where the editor is. That's the one to edit, not the copy the build puts next to the game.
    /// </summary>
    private static string? FindContent()
    {
        for (string? at = AppContext.BaseDirectory; at is not null; at = Path.GetDirectoryName(at))
        {
            if (File.Exists(Path.Combine(at, "Fighter2D.csproj")) && File.Exists(Path.Combine(at, GameContent.CHARACTERS_FILE)))
                return at;
        }

        return File.Exists(Path.Combine(AppContext.BaseDirectory, GameContent.CHARACTERS_FILE)) ? AppContext.BaseDirectory : null;
    }

    private static string? ValueOf(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
