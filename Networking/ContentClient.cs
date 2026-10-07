using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Fighter2D.Content;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// The joining machine's half of the content sync. Takes the host's list of files, works out which ones we are missing and asks for them.
/// Nothing the host says is trusted. Every path, size and hash is checked before any of it gets near our disk (see <see cref="ContentDownload"/> for the files themselves).
/// </summary>
internal sealed class ContentClient
{
    // More than this is not a game pack any more, it is somebody trying to fill a disk
    private const int MAX_FILES = 2000;
    private const long MAX_FILE_SIZE = 32 * 1024 * 1024;
    private const long MAX_TOTAL_SIZE = 256 * 1024 * 1024;

    // How many file requests fit into one message
    private const int REQUEST_BATCH = 200;
    private const int HASH_LENGTH = 64;

    private readonly NetSession _session;

    // The list of the host as it comes in, and the whole of it once it has
    private ContentFile?[] _listed = [];
    private int _listedCount;
    private List<ContentFile> _manifest = [];
    private string _manifestHash = string.Empty;

    // The files that are coming in, null while there are none
    private ContentDownload? _download;

    public ContentPhase Phase { get; private set; } = ContentPhase.Waiting;
    public string Error { get; private set; } = string.Empty;

    public long TotalBytes => _download?.TotalBytes ?? 0;
    public long DoneBytes => _download?.DoneBytes ?? 0;

    public ContentClient(NetSession session)
    {
        _session = session;
        _session.Received += OnReceived;
    }

    private void OnReceived(NetMessage id, Message message)
    {
        try
        {
            if (id == NetMessage.ContentEntry) ReceiveEntry(message);
            else if (id == NetMessage.ContentChunk) ReceiveChunk(message.GetInt(), message.GetLong(), message.GetBytes());
        }
        catch (Exception e)
        {
            // A full disk, a locked file, whatever it was. The fight is off rather than the game
            Fail(e.Message);
        }
    }

    /* The list */

    private void ReceiveEntry(Message message)
    {
        string listHash = message.GetString();
        int count = message.GetInt();
        int index = message.GetInt();
        var file = new ContentFile(message.GetString(), message.GetLong(), message.GetString());

        // The first we hear of this list. The host starts over whenever it likes, e.g. when we come back to its lobby
        if (listHash != _manifestHash || _listed.Length != count)
        {
            if (!StartList(listHash, count)) return;
        }

        // The list we already have and are done with, offered again (we came back to the lobby and said hello).
        // The host has forgotten that we have it by now, so it gets told once more
        if (Phase == ContentPhase.Ready && index == 0)
        {
            Acknowledge();
            return;
        }

        if (Phase != ContentPhase.Waiting || index < 0 || index >= count || _listed[index] is not null) return;

        // Nothing the host says gets to decide where on our disk things go, or how much of it
        long listedSize = _listed.Sum(known => known?.Size ?? 0);
        if (!GameContent.IsContentPath(file.Path) || file.Size < 0 || file.Size > MAX_FILE_SIZE || listedSize + file.Size > MAX_TOTAL_SIZE)
        {
            Fail($"the host listed a file we will not take ('{file.Path}')");
            return;
        }

        _listed[index] = file;
        if (++_listedCount < count) return;

        _manifest = [.. _listed.Select(listed => listed!.Value)];
        Resolve();
    }

    /// <summary>
    /// Helper method to start taking down a new list, false if the host is talking shit.
    /// </summary>
    private bool StartList(string listHash, int count)
    {
        if (count <= 0 || count > MAX_FILES || listHash.Length != HASH_LENGTH || !listHash.All(char.IsAsciiHexDigit))
        {
            Fail("the host has content that makes no sense");
            return false;
        }

        _download?.Dispose();
        _download = null;
        Phase = ContentPhase.Waiting;

        _manifestHash = listHash;
        _listed = new ContentFile?[count];
        _listedCount = 0;
        return true;
    }

    /// <summary>
    /// Called once the host has listed everything it has. Works out what we are missing and asks for it.
    /// </summary>
    private void Resolve()
    {
        // Having the same content as the host is the usual case and needs nothing at all
        if (ContentManifest.Hash(ContentManifest.Build(GameContent.Home)) == _manifestHash)
        {
            GameContent.UseLocal();
            Finish("our own");
            return;
        }

        _download = new ContentDownload(_manifest, Path.Combine(GameContent.CACHE_DIRECTORY, _manifestHash[..16]));

        List<int> missing = _download.FindMissing();
        if (missing.Count == 0)
        {
            Complete();
            return;
        }

        Console.WriteLine($"[Content] Asking the host for {missing.Count} files ({_download.TotalBytes / 1024} KB).");
        Phase = ContentPhase.Downloading;

        // In batches, a long list would not fit into one message
        foreach (int[] batch in missing.Chunk(REQUEST_BATCH))
        {
            var request = NetSession.Reliable(NetMessage.ContentRequest);
            request.AddInt(batch.Length);
            foreach (int index in batch) request.AddInt(index);
            _session.Send(request);
        }

        // Empty files never get a chunk sent, so they can already be all there is
        if (_download.IsDone) Complete();
    }

    /* The files */

    private void ReceiveChunk(int index, long offset, byte[] data)
    {
        if (Phase != ContentPhase.Downloading || _download is null) return;

        if (!_download.Write(index, offset, data, out string problem))
        {
            Fail(problem);
            return;
        }

        if (_download.IsDone) Complete();
    }

    /// <summary>
    /// Called once everything is in the cache. Checks all of it one more time and switches the game over to it.
    /// </summary>
    private void Complete()
    {
        string cache = _download!.Directory;

        if (ContentManifest.Hash(ContentManifest.Build(cache)) != _manifestHash)
        {
            Fail("what we ended up with is not what the host has");
            return;
        }

        GameContent.Use(cache);
        Finish("the hosts");
    }

    private void Finish(string whose)
    {
        Phase = ContentPhase.Ready;
        Console.WriteLine($"[Content] Playing with {whose} content ({_manifestHash[..12]}).");

        Acknowledge();
    }

    /// <summary>
    /// Helper method to tell the host we have its content, which is what it waits for before it lets us say we are ready.
    /// </summary>
    private void Acknowledge()
    {
        var ready = NetSession.Reliable(NetMessage.ContentReady);
        ready.AddString(_manifestHash);
        _session.Send(ready);
    }

    private void Fail(string reason)
    {
        Phase = ContentPhase.Failed;
        Error = reason;
        Console.WriteLine($"[Content] Giving up: {reason}.");

        _download?.Dispose();
    }
}
