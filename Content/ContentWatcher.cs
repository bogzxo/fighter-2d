using Horizon.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Fighter2D.Content;

/// <summary>
/// Keeps an eye on the content of a game started with --content, so a move saved in Hex is in the fight a moment later
/// without anybody opening the pause menu for the reload. Only our own content (see <see cref="GameContent.Home"/>), only
/// the content folders and the kinds of file content is, and a change only counts once the files have been left alone a
/// moment, Hex and Aseprite both write a file in more than one go.
/// </summary>
internal static class ContentWatcher
{
    // How long the files have to be left alone before a change counts, in milliseconds
    private const long QUIET_MS = 400;

    private static readonly List<FileSystemWatcher> Watchers = [];
    private static long _changedAt = -1;

    /// <summary>Starts watching our own content, once, for a game started with --content.</summary>
    public static void Start(string home)
    {
        if (Watchers.Count > 0) return;

        foreach (string folder in GameContent.Folders)
        {
            string full = Path.Combine(home, folder);
            if (!Directory.Exists(full)) continue;

            var watcher = new FileSystemWatcher(full)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Renamed += Changed;
            watcher.Deleted += Changed;
            watcher.EnableRaisingEvents = true;
            Watchers.Add(watcher);
        }

        Log.Info($"[Content] Watching '{home}', a change to the content reloads the fight.");
    }

    // On the watcher's own thread
    private static void Changed(object sender, FileSystemEventArgs e)
    {
        if (!GameContent.Extensions.Contains(Path.GetExtension(e.FullPath).ToLowerInvariant())) return;
        Interlocked.Exchange(ref _changedAt, Environment.TickCount64);
    }

    /// <summary>
    /// Whether the content changed and has been left alone since, true once a change, which is when a fight reloads it.
    /// </summary>
    public static bool TakeSettled()
    {
        long at = Interlocked.Read(ref _changedAt);
        if (at < 0 || Environment.TickCount64 - at < QUIET_MS) return false;

        return Interlocked.CompareExchange(ref _changedAt, -1, at) == at;
    }
}
