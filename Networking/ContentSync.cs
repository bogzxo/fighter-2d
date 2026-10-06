using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Fighter2D.Content;
using Riptide;

namespace Fighter2D.Networking;

internal enum ContentPhase
{
    // Nothing has been said about the content yet
    Waiting,

    // The files are on their way from the host
    Downloading,

    // Both machines have the same content, checked file by file
    Ready,

    // Something went wrong, there will be no fight
    Failed
}

/// <summary>
/// Makes sure both machines of an online fight have the same content (the moves, the characters, the maps and their art) before it starts.
/// The host says what it has, file by file with a checksum each. The other machine checks that against what it has itself and against what
/// it downloaded before, asks for whatever is missing or different, and keeps what it gets in a cache of its own: the files of the game are never touched.
/// A file that was tampered with has another checksum, so it simply gets replaced by the one of the host for the fight.
/// </summary>
internal sealed class ContentSync
{
    // How much of a file goes into one message, and how many of those go out per update
    private const int CHUNK_SIZE = 1000;
    private const int CHUNKS_PER_TICK = 24;

    // More than this is not a game pack any more, it is somebody trying to fill a disk
    private const int MAX_FILES = 2000;
    private const long MAX_FILE_SIZE = 32 * 1024 * 1024;
    private const long MAX_TOTAL_SIZE = 256 * 1024 * 1024;

    private const string PARTIAL_SUFFIX = ".part";

    private readonly NetSession _session;

    // What the host has, in the order it listed it. On the host this is its own content
    private List<ContentFile> _manifest = [];
    private string _manifestHash = string.Empty;

    // Host: what is still to be sent, as (file, how far into it we are)
    private readonly Queue<int> _sendQueue = new();
    private FileStream? _sending;
    private int _sendingIndex = -1;
    private readonly byte[] _chunk = new byte[CHUNK_SIZE];

    // Client: the list of the host as it comes in, the files that are coming in, and where they go
    private ContentFile?[] _listed = [];
    private int _listedCount;
    private string _cacheDirectory = string.Empty;
    private readonly Dictionary<int, (FileStream Stream, long Received)> _receiving = [];
    private int _filesLeft;

    public ContentPhase Phase { get; private set; } = ContentPhase.Waiting;

    /// <summary>
    /// What went wrong, for a sync that failed.
    /// </summary>
    public string Error { get; private set; } = string.Empty;

    /// <summary>
    /// Host only: whether the other machine has said it has our content, down to the checksum of all of it.
    /// </summary>
    public bool PeerVerified { get; private set; }

    /// <summary>
    /// How many bytes had to be sent across, and how many of them have been.
    /// </summary>
    public long TotalBytes { get; private set; }
    public long DoneBytes { get; private set; }

    /// <summary>
    /// How far along the transfer is, from 0 to 1.
    /// </summary>
    public float Progress => TotalBytes > 0 ? Math.Clamp(DoneBytes / (float)TotalBytes, 0, 1) : 1;

    public bool IsTransferring => TotalBytes > 0 && DoneBytes < TotalBytes;

    public ContentSync(NetSession session)
    {
        _session = session;
        _session.Received += OnReceived;
        _session.Ticked += Tick;

        // The content of the host is the content of the fight, it has nothing to wait for
        if (_session.IsHost) Phase = ContentPhase.Ready;
    }

    /* The host */

    /// <summary>
    /// Called by the host when the other player walks in: tells them what our content is, the rest is up to what they ask for.
    /// </summary>
    public void Offer()
    {
        if (!_session.IsHost) return;

        StopSending();
        PeerVerified = false;
        TotalBytes = DoneBytes = 0;

        _manifest = GameContent.BuildManifest();
        _manifestHash = GameContent.HashManifest(_manifest);

        // One message per file, the whole list would not fit into one. Each of them says which list it is part of,
        // as nothing promises they arrive in the order they were sent in
        for (int i = 0; i < _manifest.Count; i++)
        {
            var entry = NetSession.Reliable(NetMessage.ContentEntry);
            entry.AddString(_manifestHash);
            entry.AddInt(_manifest.Count);
            entry.AddInt(i);
            entry.AddString(_manifest[i].Path);
            entry.AddLong(_manifest[i].Size);
            entry.AddString(_manifest[i].Hash);
            _session.Send(entry);
        }

        Console.WriteLine($"[Content] Offered {_manifest.Count} files ({_manifestHash[..12]}).");
    }

    /// <summary>
    /// Called once per update, this is where the files go out a few pieces at a time so the connection is not swamped.
    /// </summary>
    private void Tick()
    {
        if (!_session.IsHost) return;

        for (int sent = 0; sent < CHUNKS_PER_TICK; sent++)
        {
            if (_sending is null)
            {
                if (_sendQueue.Count == 0) return;

                _sendingIndex = _sendQueue.Dequeue();
                _sending = File.OpenRead(_manifest[_sendingIndex].Path);
            }

            long offset = _sending.Position;
            int read = _sending.Read(_chunk, 0, CHUNK_SIZE);

            if (read > 0)
            {
                var message = NetSession.Reliable(NetMessage.ContentChunk);
                message.AddInt(_sendingIndex);
                message.AddLong(offset);
                message.AddBytes(_chunk.AsSpan(0, read).ToArray());
                _session.Send(message);

                DoneBytes += read;
            }

            if (read < CHUNK_SIZE)
            {
                _sending.Dispose();
                _sending = null;
            }
        }
    }

    /// <summary>
    /// Called by the host when the other player leaves: whoever comes next has to show they have our content all over again.
    /// </summary>
    public void Forget()
    {
        if (!_session.IsHost) return;

        StopSending();
        PeerVerified = false;
        TotalBytes = DoneBytes = 0;
    }

    private void StopSending()
    {
        _sending?.Dispose();
        _sending = null;
        _sendQueue.Clear();
    }

    /* The other player */

    /// <summary>
    /// Called once the host has listed everything it has: works out what we are missing and asks for it.
    /// </summary>
    private void Resolve()
    {
        // The same content as the host is the usual case, and needs nothing at all
        if (GameContent.HashManifest(GameContent.BuildManifest()) == _manifestHash)
        {
            GameContent.UseLocal();
            Finish("our own");
            return;
        }

        _cacheDirectory = Path.Combine(GameContent.CACHE_DIRECTORY, _manifestHash[..16]);
        Directory.CreateDirectory(_cacheDirectory);

        // The cache ends up with a full copy of the hosts content: what we have that is the same is copied over,
        // what we got from them before is still there, and the rest is what we ask for
        var wanted = _manifest.Select(file => file.Path).ToHashSet();
        foreach (ContentFile stray in GameContent.BuildManifest(_cacheDirectory).Where(file => !wanted.Contains(file.Path)))
        {
            File.Delete(Path.Combine(_cacheDirectory, stray.Path));
        }

        var missing = new List<int>();
        for (int i = 0; i < _manifest.Count; i++)
        {
            ContentFile file = _manifest[i];
            string cached = Path.Combine(_cacheDirectory, file.Path);

            if (File.Exists(cached) && GameContent.HashFile(cached) == file.Hash) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);

            if (File.Exists(file.Path) && GameContent.HashFile(file.Path) == file.Hash)
            {
                File.Copy(file.Path, cached, overwrite: true);
                continue;
            }

            missing.Add(i);
            TotalBytes += file.Size;
        }

        if (missing.Count == 0)
        {
            Complete();
            return;
        }

        Console.WriteLine($"[Content] Asking the host for {missing.Count} files ({TotalBytes / 1024} KB).");
        Phase = ContentPhase.Downloading;
        _filesLeft = missing.Count;

        // In batches, a long list would not fit into one message
        foreach (int[] batch in missing.Chunk(200))
        {
            var request = NetSession.Reliable(NetMessage.ContentRequest);
            request.AddInt(batch.Length);
            foreach (int index in batch) request.AddInt(index);
            _session.Send(request);
        }

        // An empty file never gets a piece sent, so there is nothing to wait for
        foreach (int index in missing.Where(index => _manifest[index].Size == 0).ToArray())
        {
            File.WriteAllBytes(Path.Combine(_cacheDirectory, _manifest[index].Path), []);
            _filesLeft--;
        }

        if (_filesLeft == 0) Complete();
    }

    private void ReceiveChunk(int index, long offset, byte[] data)
    {
        if (Phase != ContentPhase.Downloading || index < 0 || index >= _manifest.Count) return;

        ContentFile file = _manifest[index];
        if (offset < 0 || offset + data.Length > file.Size)
        {
            Fail($"the host sent more of '{file.Path}' than there is of it");
            return;
        }

        string target = Path.Combine(_cacheDirectory, file.Path);

        if (!_receiving.TryGetValue(index, out var incoming))
        {
            incoming = (new FileStream(target + PARTIAL_SUFFIX, FileMode.Create, FileAccess.Write), 0);
        }

        // Pieces can overtake each other, each one says where it belongs
        incoming.Stream.Position = offset;
        incoming.Stream.Write(data);
        incoming.Received += data.Length;
        DoneBytes += data.Length;

        if (incoming.Received < file.Size)
        {
            _receiving[index] = incoming;
            return;
        }

        incoming.Stream.Dispose();
        _receiving.Remove(index);

        // What arrived has to be what was promised, anything else is not going anywhere near the game
        if (GameContent.HashFile(target + PARTIAL_SUFFIX) != file.Hash)
        {
            File.Delete(target + PARTIAL_SUFFIX);
            Fail($"'{file.Path}' did not arrive in one piece");
            return;
        }

        File.Move(target + PARTIAL_SUFFIX, target, overwrite: true);

        if (--_filesLeft == 0) Complete();
    }

    /// <summary>
    /// Called once everything is in the cache: checks all of it one more time and switches the game over to it.
    /// </summary>
    private void Complete()
    {
        if (GameContent.HashManifest(GameContent.BuildManifest(_cacheDirectory)) != _manifestHash)
        {
            Fail("what we ended up with is not what the host has");
            return;
        }

        GameContent.Use(_cacheDirectory);
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

        foreach (var (stream, _) in _receiving.Values) stream.Dispose();
        _receiving.Clear();
    }

    /* Both */

    private void OnReceived(NetMessage id, Message message)
    {
        try
        {
            if (_session.IsHost)
            {
                ReceiveAsHost(id, message);
            }
            else
            {
                ReceiveAsClient(id, message);
            }
        }
        catch (Exception e)
        {
            // A disk that is full, a file that is locked: whatever it was, the fight is off rather than the game
            Fail(e.Message);
        }
    }

    private void ReceiveAsHost(NetMessage id, Message message)
    {
        switch (id)
        {
            case NetMessage.ContentRequest:
                int count = message.GetInt();
                for (int i = 0; i < count; i++)
                {
                    int index = message.GetInt();
                    if (index < 0 || index >= _manifest.Count || _sendQueue.Contains(index)) continue;

                    _sendQueue.Enqueue(index);
                    TotalBytes += _manifest[index].Size;
                }
                break;

            case NetMessage.ContentReady:
                PeerVerified = message.GetString() == _manifestHash;
                if (!PeerVerified) Console.WriteLine("[Content] The other player says they are ready with content that is not ours.");
                break;
        }
    }

    private void ReceiveAsClient(NetMessage id, Message message)
    {
        switch (id)
        {
            case NetMessage.ContentEntry:
                string listHash = message.GetString();
                int count = message.GetInt();
                int index = message.GetInt();
                var file = new ContentFile(message.GetString(), message.GetLong(), message.GetString());

                if (listHash != _manifestHash || _listed.Length != count)
                {
                    // The first we hear of this list. The host starts over whenever it likes, e.g. when we come back to its lobby
                    if (count <= 0 || count > MAX_FILES || listHash.Length != 64 || !listHash.All(char.IsAsciiHexDigit))
                    {
                        Fail("the host has content that makes no sense");
                        break;
                    }

                    foreach (var (stream, _) in _receiving.Values) stream.Dispose();
                    _receiving.Clear();
                    TotalBytes = DoneBytes = 0;
                    Phase = ContentPhase.Waiting;

                    _manifestHash = listHash;
                    _listed = new ContentFile?[count];
                    _listedCount = 0;
                }

                // The list we already have and are done with, offered again (we came back to the lobby and said hello).
                // The host has forgotten that we have it by now, so it is told once more: once per list, on its first file
                if (Phase == ContentPhase.Ready && index == 0)
                {
                    Acknowledge();
                    break;
                }

                if (Phase != ContentPhase.Waiting || index < 0 || index >= count || _listed[index] is not null) break;

                // Nothing the host says gets to decide where on our disk things go, or how much of it
                if (!GameContent.IsContentPath(file.Path) || file.Size < 0 || file.Size > MAX_FILE_SIZE
                    || _listed.Sum(known => known?.Size ?? 0) + file.Size > MAX_TOTAL_SIZE)
                {
                    Fail($"the host listed a file we will not take ('{file.Path}')");
                    break;
                }

                _listed[index] = file;
                if (++_listedCount == count)
                {
                    _manifest = [.. _listed.Select(listed => listed!.Value)];
                    Resolve();
                }
                break;

            case NetMessage.ContentChunk:
                ReceiveChunk(message.GetInt(), message.GetLong(), message.GetBytes());
                break;
        }
    }
}
