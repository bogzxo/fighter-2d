using Horizon.Logging;
using System;
using System.IO;
using System.Reflection;

namespace Fighter2D.Content;

/// <summary>
/// A published build is one exe and its Assets folder.
/// Everything else it needs as a file (native libraries, shaders, fonts) is packed into the exe and gets unpacked next to it the first time it runs.
/// See the PackEngineFiles target in Fighter2D.csproj for the packing half.
/// </summary>
internal static class PackedFiles
{
    // What the name of every packed file starts with, the rest of the name is where it goes
    private const string PREFIX = "packed/";

    /// <summary>
    /// Helper method to unpack whatever the exe carries, and to make the game look for its files next to the exe wherever it was started from.
    /// Has to be called before anything else is.
    /// </summary>
    public static void Unpack()
    {
        string home = AppContext.BaseDirectory;

        // Assets, shaders and the rest are all found from here
        Directory.SetCurrentDirectory(home);

        Assembly assembly = typeof(PackedFiles).Assembly;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(PREFIX, StringComparison.Ordinal)) continue;

            string target = Path.Combine(home, name[PREFIX.Length..].Replace('/', Path.DirectorySeparatorChar));

            using Stream? packed = assembly.GetManifestResourceStream(name);
            if (packed is null) continue;

            // Already there from an earlier run
            if (File.Exists(target) && new FileInfo(target).Length == packed.Length) continue;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using FileStream file = File.Create(target);
                packed.CopyTo(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Somewhere we can't write to. Whatever needs the file will complain about it better than we can
                Log.Warning($"'{target}' couldn't be unpacked: {exception.Message}");
            }
        }
    }
}
