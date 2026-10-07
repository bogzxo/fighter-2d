using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Fighter2D.Content;

/// <summary>
/// One file of the content. Where it is (relative to the root of the content, with forward slashes), how big it is and the hash of what is in it.
/// </summary>
internal readonly record struct ContentFile(string Path, long Size, string Hash);

/// <summary>
/// Lists and hashes the content, which is how two machines find out whether they have the same game.
/// </summary>
internal static class ContentManifest
{
    /// <summary>
    /// Helper method to list every content file in a folder along with its hash, in an order that is the same on every machine.
    /// </summary>
    /// <param name="root">The folder to look in, empty for the one the game came with.</param>
    public static List<ContentFile> Build(string root = "")
    {
        var files = new List<ContentFile>();

        foreach (string folder in GameContent.Folders)
        {
            string directory = root.Length == 0 ? folder : Path.Combine(root, folder);
            if (!Directory.Exists(directory)) continue;

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root.Length == 0 ? "." : root, file).Replace('\\', '/');
                if (!GameContent.IsContentPath(relative)) continue;

                files.Add(new ContentFile(relative, new FileInfo(file).Length, HashFile(file)));
            }
        }

        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return files;
    }

    /// <summary>
    /// One hash for a whole manifest. Two machines with the same one have the same content.
    /// </summary>
    public static string Hash(IEnumerable<ContentFile> files)
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
