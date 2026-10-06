using System;
using System.Numerics;

using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.Logic;
using Fighter2D.Match;

using Horizon.Core;
using Horizon.Core.Components;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// The networking of an online fight. Both machines play their own player and tell the other one which buttons they held
/// on every tick of it, so the player of the other machine is played here the same way ours is: by their buttons.
/// A few times a second each also says how its player is really doing, and wherever that has come apart from what the
/// other one made of the buttons it is put right (see <see cref="PlayerController.Reconcile"/>).
/// </summary>
internal sealed class FightNetwork(NetSession session, RoundDirector round) : IGameComponent, IDisposable
{
    // How many ticks of the fight go by between two states of our player, the buttons go out on every one of them
    private const uint STATE_INTERVAL = 6;

    // How long (in seconds) a state waits for the other player to get to the tick it is from before it is gone by regardless
    private const float STATE_PATIENCE = 0.25f;

    // How often (in seconds) the host says how the match stands without anything having changed, which keeps the clocks together
    private const float ROUND_INTERVAL = 1.0f;

    /// <summary>
    /// The player this machine plays, and the one the other machine does.
    /// </summary>
    public required Player LocalPlayer { get; init; }
    public required Player RemotePlayer { get; init; }

    private float _roundTimer;

    // The ticks of our player the other machine was last told the buttons and the state of
    private uint _inputTick, _stateTick;

    // How many of our blows the other machine was told of, and how many of theirs we have heard of
    private byte _hitsSent, _hitsSeen;

    // What the other machine last said about its player, kept until that player has got to the tick it is from
    private PlayerSnapshot? _pending;
    private float _pendingTime;

    public bool IsConnected => session.IsConnected;

    private PlayerController? Local => LocalPlayer?.Controller is { Player: not null, StateTracker: not null } controller ? controller : null;
    private PlayerController? Remote => RemotePlayer?.Controller is { Player: not null, StateTracker: not null } controller ? controller : null;

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

        SendPlayer();
        ApplyPending(dt);
        if (session.IsHost) SendRound(dt);
    }

    private void SendPlayer()
    {
        if (Local is not { } controller || controller.Tick == _inputTick) return;
        _inputTick = controller.Tick;

        // Unreliable on purpose, every one of these repeats what the last few said
        var message = NetSession.Unreliable(NetMessage.PlayerInput);
        InputReport.Of(controller).Write(message);
        session.Send(message);

        if (controller.Tick - _stateTick >= STATE_INTERVAL) SendState(reliable: false);
    }

    /// <summary>
    /// Helper method to say how our player is doing.
    /// </summary>
    /// <param name="reliable">Whether it has to get there: what a blow did to us does, where we are a moment later is old news by the time it could be sent again.</param>
    private void SendState(bool reliable)
    {
        if (Local is not { } controller) return;
        _stateTick = controller.Tick;

        var message = reliable ? NetSession.Reliable(NetMessage.PlayerState) : NetSession.Unreliable(NetMessage.PlayerState);
        PlayerSnapshot.Of(LocalPlayer, _hitsSeen).Write(message);
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
    /// Called when our player lands a move on the player of the other machine, what comes of it is up to the machine that plays them.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    /// <param name="impact">Where it landed.</param>
    public void SendHit(string move, float direction, Vector2 impact)
    {
        var message = NetSession.Reliable(NetMessage.Hit);
        new HitReport(move, direction, impact).Write(message);
        session.Send(message);

        // Whatever they said about themselves before they hear of this is out of date, see ApplyPlayer
        _hitsSent++;
        _pending = null;
    }

    private void OnMessageReceived(NetMessage id, Message message)
    {
        switch (id)
        {
            case NetMessage.PlayerInput:
                InputReport input = InputReport.Read(message);
                (RemotePlayer as NetworkPlayer)?.Input.Receive(input.Tick, input.NewestFirst);
                break;

            case NetMessage.PlayerState:
                ApplyPlayer(PlayerSnapshot.Read(message));
                break;

            case NetMessage.Hit:
                ReceiveHit(HitReport.Read(message));
                break;

            case NetMessage.RoundState when !session.IsHost:
                // The host counts itself as player one, on this machine that is us
                round.Apply(RoundSnapshot.Read(message).Mirrored());
                break;
        }
    }

    /// <summary>
    /// Called when the player of the other machine says they landed a move on us, whether it counts is ours to decide.
    /// </summary>
    private void ReceiveHit(in HitReport hit)
    {
        _hitsSeen++;

        // The move is one of theirs, which with another character isn't one of ours
        if (Local is { } victim && Remote is { } attacker && attacker.MoveList.TryGetMove(hit.Move, out var move))
        {
            Combat.Resolve(attacker, victim, move, hit.Direction, hit.Impact, predicted: false);
        }

        // They made a guess at what it did to us, this is what it did
        SendState(reliable: true);
    }

    private void ApplyPlayer(in PlayerSnapshot snapshot)
    {
        // Messages can beat the player to it, the controller has no player until the scene has set it up
        if (Remote is null) return;

        // Their health is theirs to keep track of, we only show it
        RemotePlayer.Health = snapshot.Health;

        // From before they heard of our last blow: by this they are still standing there as if nothing had happened
        if (snapshot.HitsSeen != _hitsSent) return;

        // One at a time, each waits for the player to get to where it was taken down
        if (_pending is not null) return;

        _pending = snapshot;
        _pendingTime = 0.0f;
    }

    /// <summary>
    /// Helper method to hold what the other machine said about its player against what we made of their buttons, once
    /// we have pressed the ones of the tick it is from. Any sooner and it would be put right for what it hasn't done yet.
    /// </summary>
    private void ApplyPending(float dt)
    {
        if (_pending is not { } snapshot || Remote is not { } controller) return;

        _pendingTime += dt;

        // The difference is taken as a signed one so it survives the counter wrapping
        bool caughtUp = RemotePlayer is NetworkPlayer player && (int)(player.Input.Tick - snapshot.Tick) >= 0;
        if (!caughtUp && _pendingTime < STATE_PATIENCE) return;

        controller.Reconcile(snapshot);
        _pending = null;
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
