using System;

using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.Match;

using Horizon.Core;
using Horizon.Core.Components;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// The networking of an online fight. Both machines play their own player and tell the other one about it sixty times a second,
/// the player of the other machine is a puppet that goes wherever it is told.
/// </summary>
internal sealed class FightNetwork(NetSession session, RoundDirector round) : IGameComponent, IDisposable
{
    private const float SEND_INTERVAL = 1 / 60.0f;

    // How often (in seconds) the host says how the match stands without anything having changed, which keeps the clocks together
    private const float ROUND_INTERVAL = 1.0f;

    /// <summary>
    /// The player this machine plays, and the one the other machine does.
    /// </summary>
    public required Player LocalPlayer { get; init; }
    public required Player RemotePlayer { get; init; }

    private float _sendTimer, _roundTimer;

    public bool IsConnected => session.IsConnected;

    public void Initialize()
    {
        session.Received += OnMessageReceived;
        session.PeerLeft += OnPeerLeft;

        // The other machine hears about every turn the match takes the moment it takes it
        if (session.IsHost) round.PhaseChanged += OnPhaseChanged;
    }

    public void UpdateState(float dt)
    {
        if (!session.IsConnected) return;

        SendPlayer(dt);
        if (session.IsHost) SendRound(dt);
    }

    private void SendPlayer(float dt)
    {
        _sendTimer += dt;
        if (_sendTimer < SEND_INTERVAL) return;
        _sendTimer = 0;

        if (LocalPlayer?.Controller?.StateTracker is null) return;

        // Unreliable on purpose, a state that got lost is old news by the time it could be sent again
        var message = NetSession.Unreliable(NetMessage.PlayerState);
        PlayerSnapshot.Of(LocalPlayer).Write(message);
        session.Send(message);
    }

    private void SendRound(float dt)
    {
        _roundTimer += dt;
        if (_roundTimer < ROUND_INTERVAL) return;

        SendRound(reliable: false);
    }

    /// <summary>
    /// Helper method for the host to say how the match stands.
    /// </summary>
    /// <param name="reliable">Whether it has to get there: a turn the match took does, a reminder of where it stands doesn't.</param>
    private void SendRound(bool reliable)
    {
        _roundTimer = 0;

        var message = reliable ? NetSession.Reliable(NetMessage.RoundState) : NetSession.Unreliable(NetMessage.RoundState);
        round.Capture().Write(message);
        session.Send(message);
    }

    private void OnPhaseChanged(RoundPhase phase) => SendRound(reliable: true);

    /// <summary>
    /// Called when our player lands a move on the puppet, what comes of it is up to the machine that plays them.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    public void SendHit(string move, float direction)
    {
        var message = NetSession.Reliable(NetMessage.Hit);
        new HitReport(move, direction).Write(message);
        session.Send(message);
    }

    private void OnMessageReceived(NetMessage id, Message message)
    {
        switch (id)
        {
            case NetMessage.PlayerState:
                ApplyPlayer(PlayerSnapshot.Read(message));
                break;

            case NetMessage.Hit:
                HitReport hit = HitReport.Read(message);

                // Same goes for our own player
                if (LocalPlayer?.Controller is { Player: not null, StateTracker: not null } controller) controller.ReceiveHit(hit.Move, hit.Direction);
                break;

            case NetMessage.RoundState when !session.IsHost:
                // The host counts itself as player one, on this machine that is us
                round.Apply(RoundSnapshot.Read(message).Mirrored());
                break;
        }
    }

    private void ApplyPlayer(in PlayerSnapshot snapshot)
    {
        // Messages can beat the puppet to it, the controller has no player until the scene has set it up
        if (RemotePlayer?.Controller is not NetworkedPlayerController { Player: not null, StateTracker: not null } puppet) return;

        // Their health is theirs to keep track of, we only show it
        RemotePlayer.Health = snapshot.Health;

        puppet.Apply(snapshot);
    }

    private void OnPeerLeft()
    {
        Console.WriteLine("[FightNetwork] The other player rage quit!");

        // Whoever stays has won, there is nobody left to fight
        round.Forfeit(winner: 0);
    }

    public void Render(float dt, object? obj = null) { }
    public void UpdatePhysics(float dt) { }

    public void Dispose()
    {
        session.Received -= OnMessageReceived;
        session.PeerLeft -= OnPeerLeft;
        round.PhaseChanged -= OnPhaseChanged;

        // The fight is what the session was for
        session.Dispose();
    }

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Fight Network";
    public Entity Parent { get; set; } = null!;
}
