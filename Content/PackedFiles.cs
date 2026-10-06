using System;
using System.IO;
using System.Reflection;

namespace Fighter2D.Content;


internal static class PackedFiles
{
    // What the name of every packed file starts with, the rest is where it goes
    private const string PREFIX = "packed/";

    /// <summary>
    /// Helper method to unpack whatever the exe carries, and to have the game look for its files where the exe is
    /// whatever it was started from. Has to be called before anything else is.
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

            // There from an earlier run
            if (File.Exists(target) && new FileInfo(target).Length == packed.Length) continue;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using FileStream file = File.Create(target);
                packed.CopyTo(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Somewhere we can't write to. Whatever needs the file will say so better than we can
                Console.Error.WriteLine($"'{target}' couldn't be unpacked: {exception.Message}");
            }
        }
    }
}
