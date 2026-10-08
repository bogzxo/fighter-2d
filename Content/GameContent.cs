using Bogz.Logging;
using System;
using System.IO;
using System.Linq;

namespace Fighter2D.Content;

/// <summary>
/// Where the content of the game lives. Content is everything that makes the game what it is (moves, characters, maps, art) rather than how it runs.
/// Normally that is the Assets folder the game came with. In an online fight it can be a cached copy of the host's content instead.
/// </summary>
internal static class GameContent
{
    // The folders the content is in. Everything else next to them is the game itself (the UI, the fonts, the shaders)
    public static readonly string[] Folders = ["Assets/data", "Assets/maps", "Assets/sprites"];

    // The kinds of file that count as content. Anything else lying around in those folders is left alone, and never accepted from anybody
    public static readonly string[] Extensions = [".hor", ".tmx", ".tsx", ".tx", ".png", ".ase", ".aseprite"];

    // Where the content of other machines is kept once it has been downloaded, one folder per set
    public const string CACHE_DIRECTORY = "cache/content";

    public const string MAPS_FILE = "Assets/data/maps.hor";
    public const string CHARACTERS_FILE = "Assets/data/characters.hor";
    public const string MAPS_DIRECTORY = "Assets/maps";

    private const int MAX_PATH_LENGTH = 200;

    /// <summary>
    /// The folder our own content is in, empty for the one the game came with. Started with --content it is wherever that says,
    /// which is how the files get worked on where they are kept instead of in a copy next to the exe (see Program).
    /// </summary>
    public static string Home { get; private set; } = string.Empty;

    /// <summary>
    /// The folder the content is loaded from right now. Our own (see <see cref="Home"/>), or in an online fight the copy of the host's.
    /// </summary>
    public static string Root { get; private set; } = string.Empty;

    /// <summary>
    /// Helper method to find a file of the content, wherever the content is right now.
    /// </summary>
    /// <param name="relative">Where the file is in the content, e.g. "Assets/data/maps.hor"</param>
    public static string PathOf(string relative) => Root.Length == 0 ? relative : Path.Combine(Root, relative);

    /// <summary>
    /// Goes back to the content the game came with.
    /// </summary>
    public static void UseLocal()
    {
        if (Root != Home) Log.Info("[Content] Back to our own content.");
        Root = Home;
    }

    /// <summary>
    /// Says where our own content is from here on, for a game that was started with --content.
    /// </summary>
    public static void UseHome(string home)
    {
        Log.Info($"[Content] Our own content is in '{home}'.");
        Home = Root = home;
    }

    /// <summary>
    /// Loads the content from another folder from here on, a full copy of the content has to be in there.
    /// </summary>
    public static void Use(string root)
    {
        Log.Info($"[Content] Using the content in '{root}'.");
        Root = root;
    }

    /// <summary>
    /// Tests if a path is one a content file could have. It has to be inside the content folders, of a kind we know and not trying to climb out.
    /// This is the only thing standing between another machine and our disk, so it is picky as fuck on purpose.
    /// </summary>
    public static bool IsContentPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MAX_PATH_LENGTH) return false;
        if (path.Contains('\\') || path.Contains(':') || path.Contains("..") || path.StartsWith('/')) return false;

        foreach (char c in path)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.' or ' ')) return false;
        }

        return Folders.Any(folder => path.StartsWith(folder + "/", StringComparison.Ordinal))
            && Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
