using System;
using System.Numerics;
using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Horizon.Core;
using Horizon.Core.Components;
using Riptide;

namespace Fighter2D.Scenes;

public enum NetworkMessageId : ushort
{
    PlayerState = 1
}

internal class NetworkingManager : IGameComponent, IDisposable
{
    public required Player MasterPlayer { get; init; }
    public required Player SlavePlayer { get; init; }

    private Server? _server;
    private Client? _client;
    private readonly bool _isServer;

    private IntervalRunnerFixedStep? _intervalRunner;

    public bool IsConnected => _isServer ? (_server?.ClientCount > 0) : (_client?.IsConnected ?? false);

    /// <summary>
    /// Client constructor. Connects to server at specified address.
    /// </summary>
    public NetworkingManager(string addr)
    {
        _client = new Client();
        _isServer = false;

        _client.Connected += OnClientConnected;
        _client.Disconnected += OnClientDisconnected;
        _client.ConnectionFailed += OnClientConnectionFailed;
        _client.MessageReceived += OnMessageReceived;

        string targetAddr = addr.Contains(':') ? addr : $"{addr}:7777";
        if (!_client.Connect(targetAddr, useMessageHandlers: false))
        {
            Console.WriteLine("[NetMan] Couldn't fucking connect to server at " + targetAddr);
        }
        else
        {
            Console.WriteLine("[NetMan] Connecting to server at " + targetAddr + "...");
        }
    }

    /// <summary>
    /// Server constructor. Listens on default port 7777.
    /// </summary>
    public NetworkingManager()
    {
        _server = new Server();
        _isServer = true;

        _server.ClientConnected += OnServerClientConnected;
        _server.ClientDisconnected += OnServerClientDisconnected;
        _server.MessageReceived += OnMessageReceived;

        _server.Start(7777, 2, useMessageHandlers: false);
        Console.WriteLine("[NetMan] Server started on port 7777. Waiting for someone to join the lobby...");
    }

    private void OnClientConnected(object? sender, EventArgs e)
    {
        Console.WriteLine("[NetMan] Client successfully connected to host!");
    }

    private void OnClientDisconnected(object? sender, EventArgs e)
    {
        Console.WriteLine("[NetMan] Disconnected from host server.");
    }

    private void OnClientConnectionFailed(object? sender, EventArgs e)
    {
        Console.WriteLine("[NetMan] Client connection failed!");
    }

    private void OnServerClientConnected(object? sender, ServerConnectedEventArgs e)
    {
        Console.WriteLine($"[NetMan] Client {e.Client.Id} joined the fight!");
    }

    private void OnServerClientDisconnected(object? sender, ServerDisconnectedEventArgs e)
    {
        Console.WriteLine($"[NetMan] Client {e.Client.Id} rage quit!");
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        // test riptide properties
        ushort id = e.Message.GetUShort();
        ReadPlayerData(e.Message);
    }

    public void Initialize()
    {
        _intervalRunner = new IntervalRunnerFixedStep(1 / 60.0f, FixedUpdate);
    }

    private void FixedUpdate()
    {
        HandleNetworking();

        if (_isServer) _server?.Update();
        else _client?.Update();
    }

    private void HandleNetworking()
    {
        if (MasterPlayer == null || SlavePlayer == null) return;

        var msg = Message.Create(MessageSendMode.Unreliable, (ushort)NetworkMessageId.PlayerState);
        GeneratePlayerData(msg);

        if (_isServer)
        {
            if (_server != null && _server.ClientCount > 0)
                _server.SendToAll(msg);
        }
        else
        {
            if (_client != null && _client.IsConnected)
                _client.Send(msg);
        }
    }

    private void GeneratePlayerData(Message msg)
    {
        Vector2 pos = MasterPlayer.Transform.Position;
        Vector2 vel = MasterPlayer.PhysicsBody?.Velocity ?? Vector2.Zero;
        bool flipped = MasterPlayer.Flipped;
        short moveId = (short)(MasterPlayer.Controller?.CurrentMove?.Id ?? (MoveId)(-1));
        string anim = MasterPlayer.Controller?.ActiveAnimation ?? "idle";
        byte stance = (byte)(MasterPlayer.Controller?.StateTracker?.CurrentStance ?? Stance.Standing);
        byte status = (byte)(MasterPlayer.Controller?.StateTracker?.CurrentStatus ?? PlayerStatusType.Normal);
        bool isGrounded = MasterPlayer.Controller?.StateTracker?.IsGrounded ?? true;

        msg.AddFloat(pos.X);
        msg.AddFloat(pos.Y);
        msg.AddFloat(vel.X);
        msg.AddFloat(vel.Y);
        msg.AddBool(flipped);
        msg.AddShort(moveId);
        msg.AddString(anim);
        msg.AddByte(stance);
        msg.AddByte(status);
        msg.AddBool(isGrounded);
    }

    private void ReadPlayerData(Message msg)
    {
        if (SlavePlayer?.Controller is NetworkedPlayerController netController)
        {
            Vector2 pos = new(msg.GetFloat(), msg.GetFloat());
            Vector2 vel = new(msg.GetFloat(), msg.GetFloat());
            bool flipped = msg.GetBool();
            MoveId moveId = (MoveId)msg.GetShort();
            string anim = msg.GetString();
            Stance stance = (Stance)msg.GetByte();
            PlayerStatusType status = (PlayerStatusType)msg.GetByte();
            bool isGrounded = msg.GetBool();

            netController.ApplyNetworkState(pos, vel, flipped, moveId, anim, stance, status, isGrounded);
        }
    }

    public void Render(float dt, object? obj = null) { }
    public void UpdateState(float dt) { }
    public void UpdatePhysics(float dt) { }

    public void Dispose()
    {
        if (_isServer)
        {
            _server?.Stop();
        }
        else
        {
            _client?.Disconnect();
        }
    }

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "NetMan";
    public Entity Parent { get; set; } = null!;
}