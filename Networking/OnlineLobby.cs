using System;
using System.Collections.Generic;
using System.Text;

using Fighter2D.Match;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// What the two players of an online fight agree on before it starts:
/// 1. who takes which side.
/// 2. whether they are ready.
/// 3. the map, and the rules the match is played by.
/// The host decides everything. The other player only asks (to swap corners, to be ready).
/// </summary>
internal sealed class OnlineLobby
{
    private const byte INPUT_SWAP = 0;
    private const byte INPUT_READY = 1;
    private const byte INPUT_NOT_READY = 2;
    private const byte INPUT_CHARACTER = 3;

    private readonly NetSession _session;

    // How things stand, as the host sees it. The corners are 0 for the left (blue) one and 1 for the right (red) one
    private int _hostSide = 0;
    private bool _hostReady, _clientReady;

    // The character each of them picked, by its name in the content. Empty until they have
    private string _hostCharacter = string.Empty, _clientCharacter = string.Empty;

    public bool IsHost => _session.IsHost;

    /// <summary>
    /// Makes sure the other player has the same content we do before anybody gets to say they are ready.
    /// </summary>
    public ContentSync Content { get; }

    /// <summary>
    /// Whether the other player is in the lobby with us. For the host that is once they have said hello, not just once they are connected.
    /// </summary>
    public bool PeerPresent { get; private set; }

    /// <summary>
    /// Whether the host has gone off to choose the map.
    /// </summary>
    public bool ChoosingMap { get; private set; }

    /// <summary>
    /// Whether there is no lobby any more: the host is gone, or never was there.
    /// </summary>
    public bool IsClosed => _session.IsClosed || _session.HasFailed || (!IsHost && !_session.IsConnected);

    /// <summary>
    /// The map the host started the fight on (its file name), null until they have. This is the cue for the other player to follow.
    /// </summary>
    public string? StartedMap { get; private set; }

    /// <summary>
    /// The rules the host started the fight with, null until they have. Both machines play by these.
    /// </summary>
    public MatchRules? StartedRules { get; private set; }

    /// <summary>
    /// Goes up by one every time something about the lobby changes, for whoever wants to react to that.
    /// </summary>
    public int Revision { get; private set; }

    public int LocalSide => IsHost ? _hostSide : 1 - _hostSide;
    public bool LocalReady => IsHost ? _hostReady : _clientReady;
    public bool RemoteReady => IsHost ? _clientReady : _hostReady;
    public bool BothReady => PeerPresent && _hostReady && _clientReady;

    public string LocalCharacter => IsHost ? _hostCharacter : _clientCharacter;
    public string RemoteCharacter => IsHost ? _clientCharacter : _hostCharacter;

    public OnlineLobby(NetSession session)
    {
        _session = session;
        Content = new ContentSync(session);

        _session.Received += OnReceived;
        _session.PeerLeft += OnPeerLeft;

        // Somebody who joins is in the lobby from the moment they are connected
        PeerPresent = !IsHost;
    }

    /// <summary>
    /// Called by the other player once their screen is up, the host answers with how things stand.
    /// </summary>
    public void SayHello()
    {
        if (!IsHost) _session.Send(NetSession.Reliable(NetMessage.LobbyHello));
    }

    /// <summary>
    /// Helper method to ask for a corner. The players swap, which only happens while neither of them has said they are ready.
    /// </summary>
    public void RequestSide(int side)
    {
        if (side == LocalSide || !PeerPresent) return;

        if (IsHost)
        {
            Swap();
        }
        else
        {
            SendInput(INPUT_SWAP);
        }
    }

    /// <summary>
    /// Helper method to tell the other machine which character we picked.
    /// </summary>
    public void SetCharacter(string id)
    {
        if (IsHost)
        {
            _hostCharacter = id;
            Broadcast();
        }
        else
        {
            _clientCharacter = id;

            var message = NetSession.Reliable(NetMessage.LobbyInput);
            message.AddByte(INPUT_CHARACTER);
            message.AddString(id);
            _session.Send(message);
        }
    }

    public void SetReady(bool ready)
    {
        if (!PeerPresent) return;

        // Nobody is ready for a fight they do not have the files of
        if (ready && Content.Phase != ContentPhase.Ready) return;

        if (IsHost)
        {
            _hostReady = ready;
            Broadcast();
        }
        else
        {
            SendInput(ready ? INPUT_READY : INPUT_NOT_READY);
        }
    }

    /// <summary>
    /// Called when the host goes to (or comes back from) choosing the map, so the other player knows what they are waiting for.
    /// </summary>
    public void SetChoosingMap(bool choosing)
    {
        if (!IsHost) return;

        ChoosingMap = choosing;
        Broadcast();
    }

    /// <summary>
    /// Called by the host to start the fight on both machines.
    /// </summary>
    /// <param name="rules">What the match is played by, the other machine gets them along with the map.</param>
    public void StartFight(string mapFile, MatchRules rules)
    {
        if (!IsHost) return;

        var message = NetSession.Reliable(NetMessage.StartFight);
        message.AddString(mapFile);
        MatchRulesMessage.Write(message, rules);
        _session.Send(message);

        StartedRules = rules;
        StartedMap = mapFile;
    }

    private void Swap()
    {
        // Somebody who is ready has made up their mind
        if (_hostReady || _clientReady) return;

        _hostSide = 1 - _hostSide;
        Broadcast();
    }

    private void SendInput(byte input)
    {
        var message = NetSession.Reliable(NetMessage.LobbyInput);
        message.AddByte(input);
        _session.Send(message);
    }

    /// <summary>
    /// Helper method for the host to tell the other player how things stand.
    /// </summary>
    private void Broadcast()
    {
        Revision++;

        var message = NetSession.Reliable(NetMessage.LobbyState);
        message.AddByte((byte)_hostSide);
        message.AddBool(_hostReady);
        message.AddBool(_clientReady);
        message.AddBool(ChoosingMap);
        message.AddString(_hostCharacter);
        message.AddString(_clientCharacter);
        _session.Send(message);
    }

    private void OnReceived(NetMessage id, Message message)
    {
        switch (id)
        {
            case NetMessage.LobbyHello when IsHost:
                PeerPresent = true;
                Broadcast();

                // First things first: do they have what we have. Only asked once per player: they say hello every time they
                // come back to the lobby (from picking a character), and asking again would have us forget that they do
                if (!Content.PeerVerified) Content.Offer();
                break;

            case NetMessage.LobbyInput when IsHost:
                byte input = message.GetByte();

                if (input == INPUT_SWAP)
                {
                    Swap();
                }
                else if (input == INPUT_CHARACTER)
                {
                    _clientCharacter = message.GetString();
                    Broadcast();
                }
                else
                {
                    // Being ready takes having our content, whatever their machine says about it
                    _clientReady = input == INPUT_READY && Content.PeerVerified;
                    Broadcast();
                }
                break;

            case NetMessage.LobbyState when !IsHost:
                _hostSide = message.GetByte();
                _hostReady = message.GetBool();
                _clientReady = message.GetBool();
                ChoosingMap = message.GetBool();
                _hostCharacter = message.GetString();

                // Ours is ours to say, the host only repeats what it heard
                message.GetString();
                Revision++;
                break;

            case NetMessage.StartFight when !IsHost:
                string mapFile = message.GetString();

                // The rules first, the map is the cue to go and by then they have to be there
                StartedRules = MatchRulesMessage.Read(message);
                StartedMap = mapFile;
                break;
        }
    }

    private void OnPeerLeft()
    {
        if (!IsHost) return;

        // Back to waiting for somebody, whoever comes next starts from scratch
        PeerPresent = false;
        ChoosingMap = false;
        _hostReady = _clientReady = false;
        _clientCharacter = string.Empty;
        Content.Forget();
        Revision++;
    }
}
