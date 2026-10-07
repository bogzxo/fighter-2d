using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fighter2D.Networking;

/// <summary>
/// The files of the host's content as they land in our cache. Each file is written to a .part file first and only gets its real name once its hash checks out.
/// The cache ends up with a full copy of the host's content. Files we already have are copied over, files from last time are still there and the rest gets downloaded.
/// </summary>
internal sealed class ContentDownload : IDisposable
{
    private const string PARTIAL_SUFFIX = ".part";

    private readonly List<ContentFile> _manifest;
    private readonly Dictionary<int, (FileStream Stream, long Received)> _receiving = [];
    private int _filesLeft;

    /// <summary>
    /// The folder of the cache this copy of the host's content goes into.
    /// </summary>
    public string Directory { get; }

    public long TotalBytes { get; private set; }
    public long DoneBytes { get; private set; }

    public bool IsDone => _filesLeft == 0;

    public ContentDownload(List<ContentFile> manifest, string directory)
    {
        _manifest = manifest;
        Directory = directory;

        System.IO.Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// Helper method to work out which files of the host we still need. Everything else is in the cache by the time this returns.
    /// </summary>
    /// <returns>The files to ask the host for, by their place in its list.</returns>
    public List<int> FindMissing()
    {
        // Whatever is in the cache that the host doesn't list has no business being there
        var wanted = _manifest.Select(file => file.Path).ToHashSet();
        foreach (ContentFile stray in ContentManifest.Build(Directory).Where(file => !wanted.Contains(file.Path)))
        {
            File.Delete(Path.Combine(Directory, stray.Path));
        }

        var missing = new List<int>();
        for (int i = 0; i < _manifest.Count; i++)
        {
            ContentFile file = _manifest[i];
            string cached = Path.Combine(Directory, file.Path);

            // Still there from the last time we played this host
            if (File.Exists(cached) && ContentManifest.HashFile(cached) == file.Hash) continue;

            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(cached)!);

            // Our own copy is the same as theirs, no need to download it
            if (File.Exists(file.Path) && ContentManifest.HashFile(file.Path) == file.Hash)
            {
                File.Copy(file.Path, cached, overwrite: true);
                continue;
            }

            // An empty file never gets a chunk sent, so it is made right here
            if (file.Size == 0)
            {
                File.WriteAllBytes(cached, []);
                continue;
            }

            missing.Add(i);
            TotalBytes += file.Size;
        }

        _filesLeft = missing.Count;
        return missing;
    }

    /// <summary>
    /// Writes a chunk of a file the host sent. Once a file is complete its hash is checked and it is moved into place.
    /// </summary>
    /// <returns>False if the host sent something it shouldn't have, with <paramref name="problem"/> saying what.</returns>
    public bool Write(int index, long offset, byte[] data, out string problem)
    {
        problem = string.Empty;
        if (index < 0 || index >= _manifest.Count) return true;

        ContentFile file = _manifest[index];
        if (offset < 0 || offset + data.Length > file.Size)
        {
            problem = $"the host sent more of '{file.Path}' than there is of it";
            return false;
        }

        string target = Path.Combine(Directory, file.Path);

        if (!_receiving.TryGetValue(index, out var incoming))
        {
            incoming = (new FileStream(target + PARTIAL_SUFFIX, FileMode.Create, FileAccess.Write), 0);
        }

        // Chunks can overtake each other, each one says where it belongs
        incoming.Stream.Position = offset;
        incoming.Stream.Write(data);
        incoming.Received += data.Length;
        DoneBytes += data.Length;

        if (incoming.Received < file.Size)
        {
            _receiving[index] = incoming;
            return true;
        }

        incoming.Stream.Dispose();
        _receiving.Remove(index);

        // What arrived has to be what was promised, anything else isn't going anywhere near the game
        if (ContentManifest.HashFile(target + PARTIAL_SUFFIX) != file.Hash)
        {
            File.Delete(target + PARTIAL_SUFFIX);
            problem = $"'{file.Path}' did not arrive in one piece";
            return false;
        }

        File.Move(target + PARTIAL_SUFFIX, target, overwrite: true);
        _filesLeft--;
        return true;
    }

    public void Dispose()
    {
        foreach (var (stream, _) in _receiving.Values) stream.Dispose();
        _receiving.Clear();
    }
}
