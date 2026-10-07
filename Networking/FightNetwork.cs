using Bogz.Logging;
using System;
using System.Numerics;

using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.Combat;
using Fighter2D.Match;

using Horizon.Core;
using Horizon.Core.Components;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// The netcode of an online fight. Both machines play their own player and send the buttons they held on every tick.
/// The other machine plays those buttons back, so the network player runs through the exact same code as a local one.
/// A few times a second each side also sends a snapshot of how its player is really doing, and whatever drifted gets fixed (see PlayerReconciler).
/// It isn't rollback, but it isn't the old "send everything 60 times a second and pray" either.
/// </summary>
internal sealed class FightNetwork(NetSession session, RoundDirector round) : GameComponent, IDisposable
{
    // How many ticks go by between two snapshots of our player, the inputs go out on every single one
    private const uint SNAPSHOT_INTERVAL = 6;

    // How long (in seconds) a snapshot waits for our copy to catch up to its tick before we apply it anyway
    private const float SNAPSHOT_PATIENCE = 0.25f;

    // How often (in seconds) the host repeats how the match stands without anything having changed, which keeps the clocks together
    private const float ROUND_INTERVAL = 1.0f;

    /// <summary>
    /// The player this machine plays, and the one the other machine does.
    /// </summary>
    public required Player LocalPlayer { get; init; }
    public required Player RemotePlayer { get; init; }

    private float _roundTimer;

    // The ticks of our player the other machine was last sent the inputs and the snapshot of
    private uint _inputTick, _snapshotTick;

    // How many of our hits the other machine was told about, and how many of theirs we have heard about
    private byte _hitsSent, _hitsSeen;

    // The last snapshot of the network player, held back until our copy of them has reached the tick it is from
    private PlayerSnapshot? _pending;
    private float _pendingTime;

    public bool IsConnected => session.IsConnected;

    /// <summary>
    /// Whether the other machine has the fight paused. For as long as it does nothing moves here either, see FightScene.
    /// </summary>
    public bool RemotePaused { get; private set; }

    // Messages can show up before the scene has set the players up, a controller has no player until then
    private PlayerController? Local => LocalPlayer?.Controller is { Player: not null, StateTracker: not null } controller ? controller : null;
    private PlayerController? Remote => RemotePlayer?.Controller is { Player: not null, StateTracker: not null } controller ? controller : null;

    public override void Initialize()
    {
        session.Received += OnMessageReceived;
        session.PeerLeft += OnPeerLeft;

        // The other machine hears about every turn the match takes the moment it takes it
        if (session.IsHost) round.PhaseChanged += OnPhaseChanged;
    }

    public override void UpdateState(float dt)
    {
        if (!session.IsConnected) return;

        SendPlayer();
        ApplyPending(dt);
        if (session.IsHost) SendRound(dt);
    }

    /* Sending */

    private void SendPlayer()
    {
        if (Local is not { } controller || controller.Tick == _inputTick) return;
        _inputTick = controller.Tick;

        // Unreliable on purpose, every one of these repeats what the last few said anyway
        var message = NetSession.Unreliable(NetMessage.PlayerInput);
        InputReport.Of(controller).Write(message);
        session.Send(message);

        if (controller.Tick - _snapshotTick >= SNAPSHOT_INTERVAL) SendSnapshot(reliable: false);
    }

    /// <summary>
    /// Helper method to send how our player is really doing.
    /// </summary>
    /// <param name="reliable">Whether it has to arrive. What a hit did to us has to, a routine one is old news by the time it could be resent.</param>
    private void SendSnapshot(bool reliable)
    {
        if (Local is not { } controller) return;
        _snapshotTick = controller.Tick;

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
    /// <param name="reliable">Whether it has to arrive. A phase change has to, a reminder doesn't.</param>
    private void SendRound(bool reliable)
    {
        _roundTimer = 0;

        var message = reliable ? NetSession.Reliable(NetMessage.RoundState) : NetSession.Unreliable(NetMessage.RoundState);
        round.Capture().Write(message);
        session.Send(message);
    }

    private void OnPhaseChanged(RoundPhase phase) => SendRound(reliable: true);

    /// <summary>
    /// Called when our player lands a hit on the network player. What comes of it is up to the machine that plays them.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    /// <param name="impact">Where it landed.</param>
    public void SendHit(string move, float direction, Vector2 impact)
    {
        var message = NetSession.Reliable(NetMessage.Hit);
        new HitReport(move, direction, impact).Write(message);
        session.Send(message);

        // Any snapshot of theirs from before they hear about this hit is stale now, see ReceiveSnapshot
        _hitsSent++;
        _pending = null;
    }

    /// <summary>
    /// Called when our player swings at the network player and hits nothing. Their machine can't tell by itself, it never sees our hitboxes.
    /// </summary>
    public void SendWhiff(string move)
    {
        var message = NetSession.Reliable(NetMessage.Whiff);
        message.AddString(move);
        session.Send(message);
    }

    /// <summary>
    /// Called on the machine that didn't host when the host says to read all the data again.
    /// By then it has whatever the host changed, the host waits for that before it says so.
    /// </summary>
    public Action? ReloadRequested { get; set; }

    // Whether the fight has handed the connection on to the fight that takes its place, which is then not ours to close
    private bool _detached;

    /// <summary>
    /// Host only. Whether the other machine has said it is done reloading and back in the fight.
    /// </summary>
    public bool PeerReloaded { get; private set; }

    // The other machine only. Whether we are done reloading, which the host gets told once and again every time it asks
    private bool _reloaded;

    /// <summary>
    /// Called on the machine that didn't host once its reloaded fight is up and its fighters are back where they were.
    /// The host holds still until it hears this.
    /// </summary>
    public void SayReloaded()
    {
        _reloaded = true;
        session.Send(NetSession.Reliable(NetMessage.Reloaded));
    }

    /// <summary>
    /// Called by the host while it waits for the other machine to be done reloading. Asking again costs nothing,
    /// and the answer to the first time may have arrived while there was no fight here to hear it (we were busy reloading ourselves).
    /// </summary>
    public void AskReloaded()
    {
        session.Send(NetSession.Reliable(NetMessage.ReloadCheck));
    }

    /// <summary>
    /// Called by the host once the other machine has its data, both of them then build the fight again from what is on disk.
    /// </summary>
    public void SendReload()
    {
        session.Send(NetSession.Reliable(NetMessage.Reload));
    }

    /// <summary>
    /// Stops listening to the connection without hanging up, for a fight that is about to be replaced by another one on the same connection.
    /// </summary>
    public void Detach()
    {
        if (_detached) return;
        _detached = true;

        session.Received -= OnMessageReceived;
        session.PeerLeft -= OnPeerLeft;
        round.PhaseChanged -= OnPhaseChanged;
    }

    /// <summary>
    /// Called when our player pauses the fight or carries on with it, the other machine holds still for as long as we do.
    /// </summary>
    public void SendPause(bool paused)
    {
        var message = NetSession.Reliable(NetMessage.Pause);
        message.AddBool(paused);
        session.Send(message);
    }

    /* Receiving */

    private void OnMessageReceived(NetMessage id, Message message)
    {
        switch (id)
        {
            case NetMessage.PlayerInput:
                InputReport input = InputReport.Read(message);
                (RemotePlayer as NetworkPlayer)?.Input.Receive(input.Tick, input.NewestFirst);
                break;

            case NetMessage.PlayerState:
                ReceiveSnapshot(PlayerSnapshot.Read(message));
                break;

            case NetMessage.Hit:
                ReceiveHit(HitReport.Read(message));
                break;

            case NetMessage.Whiff:
                ReceiveWhiff(message.GetString());
                break;

            case NetMessage.Pause:
                RemotePaused = message.GetBool();
                break;

            case NetMessage.Reload when !session.IsHost:
                ReloadRequested?.Invoke();
                break;

            case NetMessage.ReloadCheck when !session.IsHost:
                // Not yet is no answer at all, the host hears from us the moment we are
                if (_reloaded) SayReloaded();
                break;

            case NetMessage.Reloaded when session.IsHost:
                PeerReloaded = true;
                break;

            case NetMessage.RoundState when !session.IsHost:
                // The host counts itself as player one, on this machine that is us
                round.Apply(RoundSnapshot.Read(message).Mirrored());
                break;
        }
    }

    /// <summary>
    /// Called when the network player says they landed a hit on us. Whether it counts is ours to decide.
    /// </summary>
    private void ReceiveHit(in HitReport hit)
    {
        _hitsSeen++;

        // The move is one of theirs, which with a different character isn't one of ours
        if (Local is { } victim && Remote is { } attacker && attacker.MoveList.TryGetMove(hit.Move, out var move))
        {
            HitResolver.Resolve(attacker, victim, move, hit.Direction, hit.Impact, predicted: false);
        }

        // They took a guess at what it did to us, this is what it actually did
        SendSnapshot(reliable: true);
    }

    /// <summary>
    /// Called when the network player says they swung at us and missed, so our HUD can call it out like theirs does.
    /// </summary>
    private void ReceiveWhiff(string moveId)
    {
        if (Remote is { } attacker && attacker.MoveList.TryGetMove(moveId, out var move))
        {
            Fight.CombatLog.Report(new AttackReport(RemotePlayer, move, HitResult.Whiff));
        }
    }

    private void ReceiveSnapshot(in PlayerSnapshot snapshot)
    {
        if (Remote is null) return;

        // Their health is theirs to keep track of, we only show it
        RemotePlayer.Health = snapshot.Health;

        // From before they heard about our last hit. Going by this one would have them stand there as if nothing happened
        if (snapshot.HitsSeen != _hitsSent) return;

        // One at a time, each waits for our copy to get to the tick it was taken on
        if (_pending is not null) return;

        _pending = snapshot;
        _pendingTime = 0.0f;
    }

    /// <summary>
    /// Helper method to apply the waiting snapshot once our copy has played the inputs of the tick it is from.
    /// Any sooner and they would be corrected for something they haven't done yet.
    /// </summary>
    private void ApplyPending(float dt)
    {
        if (_pending is not { } snapshot || Remote is not { } controller) return;

        _pendingTime += dt;

        // The difference is signed so it survives the counter wrapping around
        bool caughtUp = RemotePlayer is NetworkPlayer player && (int)(player.Input.Tick - snapshot.Tick) >= 0;
        if (!caughtUp && _pendingTime < SNAPSHOT_PATIENCE) return;

        PlayerReconciler.Apply(controller, snapshot);
        _pending = null;
    }

    private void OnPeerLeft()
    {
        Log.Info("[FightNetwork] The other player rage quit!");

        // Nobody is left to ever unpause it
        RemotePaused = false;

        // Whoever stays has won, there is nobody left to fight
        round.Forfeit(winner: 0);
    }

    public void Dispose()
    {
        // Handed on to the next fight, which is still using it
        if (_detached) return;

        session.Received -= OnMessageReceived;
        session.PeerLeft -= OnPeerLeft;
        round.PhaseChanged -= OnPhaseChanged;

        // The fight is what the session was for
        session.Dispose();
    }
}
