using System;

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
/// The host lists what it has with a hash per file. The other machine checks that against its own files and asks for whatever is missing or different.
/// What it gets is kept in a cache of its own, so the files of the game are never touched. Modded your moves to cheat? You get the host's for the fight.
/// This class is only the front of it, the two halves are <see cref="ContentHost"/> and <see cref="ContentClient"/>.
/// </summary>
internal sealed class ContentSync
{
    private readonly ContentHost? _host;
    private readonly ContentClient? _client;

    /// <summary>
    /// How far along we are with getting the host's content. The host's own content is the content of the fight, so it is always ready.
    /// </summary>
    public ContentPhase Phase => _client?.Phase ?? ContentPhase.Ready;

    /// <summary>
    /// What went wrong, for a sync that failed.
    /// </summary>
    public string Error => _client?.Error ?? string.Empty;

    /// <summary>
    /// Host only. Whether the other machine has said it has our content, down to the hash of all of it.
    /// </summary>
    public bool PeerVerified => _host?.PeerVerified ?? false;

    /// <summary>
    /// How many bytes have to go across, and how many of them have.
    /// </summary>
    public long TotalBytes => _host?.TotalBytes ?? _client?.TotalBytes ?? 0;
    public long DoneBytes => _host?.DoneBytes ?? _client?.DoneBytes ?? 0;

    /// <summary>
    /// How far along the transfer is, from 0 to 1.
    /// </summary>
    public float Progress => TotalBytes > 0 ? Math.Clamp(DoneBytes / (float)TotalBytes, 0, 1) : 1;

    public bool IsTransferring => TotalBytes > 0 && DoneBytes < TotalBytes;

    public ContentSync(NetSession session)
    {
        if (session.IsHost) _host = new ContentHost(session);
        else _client = new ContentClient(session);
    }

    /// <summary>
    /// Called by the host when the other player walks in. Tells them what our content is, the rest is up to what they ask for.
    /// </summary>
    public void Offer() => _host?.Offer();

    /// <summary>
    /// Called by the host when the other player leaves. Whoever comes next has to prove they have our content all over again.
    /// </summary>
    public void Forget() => _host?.Forget();
}
