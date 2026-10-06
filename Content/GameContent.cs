using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Fighter2D.Content;

/// <summary>
/// A file of the game's content: where it is (relative to the root of the content, with forward slashes), how big it is and what is in it.
/// </summary>
internal readonly record struct ContentFile(string Path, long Size, string Hash);

/// <summary>
/// Everything that makes the game what it is rather than how it runs
/// </summary>
internal static class GameContent
{
    // The folders the content is in, everything else next to them is the game itself (the UI, the fonts, the shaders)
    public static readonly string[] Folders = ["Assets/data", "Assets/maps", "Assets/sprites"];

    // The kinds of file that are content, whatever else lies around in those folders is left alone (and never accepted from anybody)
    public static readonly string[] Extensions = [".hor", ".tmx", ".tsx", ".tx", ".png"];

    // Where the content of other machines is kept once it has been downloaded, one folder per set
    public const string CACHE_DIRECTORY = "cache/content";

    public const string MAPS_FILE = "Assets/data/maps.hor";
    public const string CHARACTERS_FILE = "Assets/data/characters.hor";
    public const string MAPS_DIRECTORY = "Assets/maps";

    /// <summary>
    /// The folder the content is loaded from, empty for the one the game came with.
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
        if (Root.Length > 0) Console.WriteLine("[Content] Back to our own content.");
        Root = string.Empty;
    }

    /// <summary>
    /// Loads the content from another folder from here on, a full copy of the content has to be in there.
    /// </summary>
    public static void Use(string root)
    {
        Console.WriteLine($"[Content] Using the content in '{root}'.");
        Root = root;
    }

    /// <summary>
    /// Whether a path is one a file of the content could have: inside of the content folders, of a kind we know, and not trying to get out.
    /// This is what stands between another machine and our disk.
    /// </summary>
    public static bool IsContentPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 200) return false;
        if (path.Contains('\\') || path.Contains(':') || path.Contains("..") || path.StartsWith('/')) return false;

        foreach (char c in path)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.' or ' ')) return false;
        }

        return Folders.Any(folder => path.StartsWith(folder + "/", StringComparison.Ordinal))
            && Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Helper method to list every file of the content in a folder along with its checksum, in an order that is the same on every machine.
    /// </summary>
    /// <param name="root">The folder to look in, empty for the one the game came with.</param>
    public static List<ContentFile> BuildManifest(string root = "")
    {
        var files = new List<ContentFile>();

        foreach (string folder in Folders)
        {
            string directory = root.Length == 0 ? folder : Path.Combine(root, folder);
            if (!Directory.Exists(directory)) continue;

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root.Length == 0 ? "." : root, file).Replace('\\', '/');
                if (!IsContentPath(relative)) continue;

                files.Add(new ContentFile(relative, new FileInfo(file).Length, HashFile(file)));
            }
        }

        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return files;
    }

    /// <summary>
    /// One checksum for a whole manifest, two machines with the same one have the same content.
    /// </summary>
    public static string HashManifest(IEnumerable<ContentFile> files)
    {
        var text = new StringBuilder();
        foreach (ContentFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            text.Append(file.Path).Append('\n').Append(file.Hash).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
