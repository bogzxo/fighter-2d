using Horizon.Logging;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// The connection between the two machines of an online fight. Either end of it, the host listens and the other player joins.
/// It is made on the way into the lobby and lives until the players part ways, the scenes in between only borrow it.
/// </summary>
internal sealed class NetSession : IDisposable
{
    public const ushort PORT = 7777;

    // The session that is open right now, this is the one that gets pumped (see NetPump)
    public static NetSession? Active { get; private set; }

    private Server? _server;
    private Client? _client;

    public bool IsHost => _server is not null;

    /// <summary>
    /// Whether there is somebody on the other end right now.
    /// </summary>
    public bool IsConnected => IsHost ? _server!.ClientCount > 0 : _client!.IsConnected;

    /// <summary>
    /// Whether the session never got off the ground. The port was taken (host) or nobody answered (join).
    /// </summary>
    public bool HasFailed { get; private set; }

    /// <summary>
    /// Whether the session has been closed, nothing more comes out of it.
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Called for every message of the other machine, on the thread that updates the scenes.
    /// </summary>
    public event Action<NetMessage, Message>? Received;

    /// <summary>
    /// Called when the other machine goes away, however it went.
    /// </summary>
    public event Action? PeerLeft;

    /// <summary>
    /// Called once per update after the messages have come in, for whatever has things to send bit by bit.
    /// </summary>
    public event Action? Ticked;

    private NetSession() { }

    /// <summary>
    /// Helper method to start listening for the other player.
    /// </summary>
    public static NetSession Host()
    {
        var session = Open();
        session._server = new Server();

        session._server.ClientDisconnected += (_, _) => session.PeerLeft?.Invoke();
        session._server.MessageReceived += session.OnMessageReceived;

        try
        {
            // Two players and one of them is us
            session._server.Start(PORT, 1, useMessageHandlers: false);
            Log.Info($"[NetSession] Hosting on port {PORT}.");
        }
        catch (Exception e)
        {
            Log.Warning($"[NetSession] Could not host on port {PORT}: {e.Message}");
            session.HasFailed = true;
        }

        return session;
    }

    /// <summary>
    /// Helper method to go looking for a host. <see cref="IsConnected"/> or <see cref="HasFailed"/> says how that went a moment later.
    /// </summary>
    public static NetSession Join(string address)
    {
        var session = Open();
        session._client = new Client();

        session._client.ConnectionFailed += (_, _) => session.HasFailed = true;
        session._client.Disconnected += (_, _) => session.PeerLeft?.Invoke();
        session._client.MessageReceived += session.OnMessageReceived;

        string target = address.Contains(':') ? address : $"{address}:{PORT}";
        if (!session._client.Connect(target, useMessageHandlers: false))
        {
            Log.Warning($"[NetSession] '{target}' is not an address anybody could be at.");
            session.HasFailed = true;
        }

        return session;
    }

    private static NetSession Open()
    {
        // Only ever one at a time, a new one means the old one is over
        Active?.Dispose();
        return Active = new NetSession();
    }

    public static Message Reliable(NetMessage id) => Message.Create(MessageSendMode.Reliable, (ushort)id);

    public static Message Unreliable(NetMessage id) => Message.Create(MessageSendMode.Unreliable, (ushort)id);

    /// <summary>
    /// Helper method to send a message to the other machine, it is dropped if there is nobody there.
    /// </summary>
    public void Send(Message message)
    {
        if (IsClosed || !IsConnected) return;

        if (IsHost) _server!.SendToAll(message);
        else _client!.Send(message);
    }

    /// <summary>
    /// Called once per update, this is where messages come in and go out.
    /// </summary>
    public void Update()
    {
        if (IsClosed) return;

        _server?.Update();
        _client?.Update();

        Ticked?.Invoke();
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        Received?.Invoke((NetMessage)e.MessageId, e.Message);
    }

    /// <summary>
    /// The addresses another player could find this machine at, for showing to whoever is hosting.
    /// </summary>
    public static string DescribeLocalAddresses()
    {
        try
        {
            var addresses = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Take(2)
                .ToArray();

            return addresses.Length > 0 ? string.Join(" or ", addresses) : "127.0.0.1";
        }
        catch
        {
            // Not knowing our own address is no reason to stop hosting
            return "this machine";
        }
    }

    public void Dispose()
    {
        if (IsClosed) return;
        IsClosed = true;

        // Shut down sockets cleanly so ports aren't left hanging open
        _server?.Stop();
        _client?.Disconnect();

        if (Active == this) Active = null;

        // Whatever content the other machine brought goes with it
        GameContent.UseLocal();
    }
}
