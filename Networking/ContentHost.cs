using Bogz.Logging;
using System;
using System.Collections.Generic;
using System.IO;

using Fighter2D.Content;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// The host's half of the content sync. Lists what we have, then sends whichever files the other machine asks for a few chunks at a time.
/// </summary>
internal sealed class ContentHost
{
    // How much of a file goes into one message, and how many of those go out per update so the connection isn't swamped
    private const int CHUNK_SIZE = 1000;
    private const int CHUNKS_PER_TICK = 24;

    private readonly NetSession _session;
    private readonly byte[] _chunk = new byte[CHUNK_SIZE];

    // Our content, in the order it was listed in
    private List<ContentFile> _manifest = [];
    private string _manifestHash = string.Empty;

    // The files that are still to be sent (by their place in the manifest), and the one that is going out right now
    private readonly Queue<int> _sendQueue = new();
    private FileStream? _sending;
    private int _sendingIndex = -1;

    public bool PeerVerified { get; private set; }
    public long TotalBytes { get; private set; }
    public long DoneBytes { get; private set; }

    public ContentHost(NetSession session)
    {
        _session = session;
        _session.Received += OnReceived;
        _session.Ticked += SendChunks;
    }

    public void Offer()
    {
        Forget();

        _manifest = ContentManifest.Build(GameContent.Home);
        _manifestHash = ContentManifest.Hash(_manifest);

        // One message per file, the whole list would not fit into one.
        // Each of them says which list it belongs to, since nothing promises they arrive in the order they were sent in
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

        Log.Info($"[Content] Offered {_manifest.Count} files ({_manifestHash[..12]}).");
    }

    public void Forget()
    {
        _sending?.Dispose();
        _sending = null;
        _sendQueue.Clear();

        PeerVerified = false;
        TotalBytes = DoneBytes = 0;
    }

    /// <summary>
    /// Called once per update, this is where the files go out a few chunks at a time.
    /// </summary>
    private void SendChunks()
    {
        try
        {
            for (int sent = 0; sent < CHUNKS_PER_TICK; sent++)
            {
                if (_sending is null)
                {
                    if (_sendQueue.Count == 0) return;

                    _sendingIndex = _sendQueue.Dequeue();
                    _sending = File.OpenRead(Path.Combine(GameContent.Home, _manifest[_sendingIndex].Path));
                }

                SendNextChunk(_sending);
            }
        }
        catch (Exception e)
        {
            // A file that vanished or got locked under us. They don't get that file, so they never get to say they are ready
            Log.Warning($"[Content] Couldn't send a file: {e.Message}");
            Forget();
        }
    }

    private void SendNextChunk(FileStream file)
    {
        long offset = file.Position;
        int read = file.Read(_chunk, 0, CHUNK_SIZE);

        if (read > 0)
        {
            var message = NetSession.Reliable(NetMessage.ContentChunk);
            message.AddInt(_sendingIndex);
            message.AddLong(offset);
            message.AddBytes(_chunk.AsSpan(0, read).ToArray());
            _session.Send(message);

            DoneBytes += read;
        }

        // A short read means that was the end of the file
        if (read < CHUNK_SIZE)
        {
            file.Dispose();
            _sending = null;
        }
    }

    private void OnReceived(NetMessage id, Message message)
    {
        switch (id)
        {
            case NetMessage.ContentRequest:
                QueueRequested(message);
                break;

            case NetMessage.ContentReady:
                PeerVerified = message.GetString() == _manifestHash;
                if (!PeerVerified) Log.Warning("[Content] The other player says they are ready with content that is not ours.");
                break;
        }
    }

    /// <summary>
    /// Helper method to line up the files the other machine asked for.
    /// </summary>
    private void QueueRequested(Message message)
    {
        int count = message.GetInt();

        for (int i = 0; i < count; i++)
        {
            int index = message.GetInt();

            // Asking for a file we don't have or one that is already lined up gets them nothing
            if (index < 0 || index >= _manifest.Count || _sendQueue.Contains(index)) continue;

            _sendQueue.Enqueue(index);
            TotalBytes += _manifest[index].Size;
        }
    }
}
