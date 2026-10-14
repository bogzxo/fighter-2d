using System.Numerics;

using Horizon.Input;
using Horizon.Logging;
using Horizon.UI;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.Scenes;

// The fight setup screen as the lobby of an online fight. One player on this machine and one on another, a corner
// each. Locking a fighter in is saying you're ready, the two of you can swap corners until you have, and once both
// are ready the map and the rules are the host's to settle on and the fight theirs to start. Whoever joined waits
// right here and follows the host in. Everything in here does nothing at all for a fight on one machine
internal partial class FightSetupScene
{
    private const string HINT_LOBBY = "[icon:dpad] choose    [icon:pad_a] ready    [icon:pad_x] switch corners    [icon:pad_y] bindings    [icon:pad_b] leave";
    private const string HINT_LOBBY_ALONE = "[icon:dpad] choose    [icon:pad_y] bindings    [icon:pad_b] leave";

    private OnlineLobby? _lobby;
    private string _hostAddresses = string.Empty;

    // The lobby as it was when we last looked, so the panels can jump when something about it changes
    private int _revision;
    private bool _hadPeer;

    private void BuildLobby(UILayout layout)
    {
        _lobby = setup.Lobby;
        if (_lobby is not { } lobby) return;

        _hostAddresses = NetSession.DescribeLocalAddresses();

        // The host answers with how things stand (and is back from wherever it was, if that is where we come from)
        lobby.SayHello();
        lobby.SetChoosingMap(false);
        lobby.SetCharacter(setup.GetCharacter(0));

        _revision = lobby.Revision;
        _hadPeer = lobby.PeerPresent;

        // The map and the rules are the host's, whoever joined gets them along with the fight
        if (!lobby.IsHost)
        {
            foreach (string row in (string[])["map", "rounds", "time", "weather"]) layout.Get<Selector>(row).Enabled = false;
            layout.Get<Button>("btn_fight").Enabled = false;
        }

        // The game files may have to go from the host to whoever joined, which gets a progress bar of its own
        AddComponent(new HUD.ContentTransferDisplay(lobby.Content, lobby.IsHost));
    }

    /// <summary>
    /// Called every update before anything else.
    /// </summary>
    /// <returns>True if the lobby has sent us off to another scene.</returns>
    private bool UpdateLobby()
    {
        if (_lobby is not { } lobby) return false;

        if (lobby.IsClosed)
        {
            // The host is gone and the lobby with them
            Back();
            return true;
        }

        if (lobby.StartedMap is { } mapFile)
        {
            if (MapLoader.TryFind(mapFile, out MapDefinition map)) GoTo(setup.CreateFight(map), Screen.IntoFight);
            else
            {
                Log.Warning($"[Lobby] The host started on '{mapFile}', which is not a map we have.");
                Back();
            }

            return true;
        }

        // Everybody notices when somebody walks in (or out), and when the corners or who is ready change
        if (lobby.PeerPresent != _hadPeer)
        {
            _hadPeer = lobby.PeerPresent;
            _panels[1 - lobby.LocalSide].Bump(0.1f);
        }
        else if (lobby.Revision != _revision)
        {
            foreach (FighterPanel panel in _panels) panel.Bump(0.04f, 0.25f);
        }

        _revision = lobby.Revision;
        return false;
    }

    /// <summary>The corner a player of this machine is in. Online that is wherever the two of us have agreed on.</summary>
    private int CornerOf(int player) => _lobby?.LocalSide ?? player;

    /// <summary>Whether a player has locked their fighter in. Online that is being ready, which the lobby keeps.</summary>
    private bool IsLocked(int player) => _lobby is { } lobby ? player == 0 && lobby.LocalReady : _locked[player];

    private void SetLocked(int player, bool locked)
    {
        if (_lobby is { } lobby) lobby.SetReady(locked);
        else _locked[player] = locked;
    }

    /// <summary>
    /// Whether whoever is on this machine can say they are ready. Not before somebody is there to be ready for, and not
    /// before the files of the fight are here.
    /// </summary>
    private bool CanBeReady(out string why)
    {
        why = string.Empty;
        if (_lobby is not { } lobby) return true;

        if (!lobby.PeerPresent) why = "nobody to be ready for yet";
        else if (!lobby.IsHost && lobby.Content.Phase != ContentPhase.Ready) why = "the game files aren't here yet";

        return why.Length == 0;
    }

    /// <summary>Whether the fight is this machine's to start. Online only the host's, and only with both players ready.</summary>
    private bool CanStartFight => _lobby is not { } lobby || (lobby.IsHost && lobby.BothReady);

    /// <summary>Whether the map and the rules are this machine's to change.</summary>
    private bool CanChangeMatch => _lobby is not { IsHost: false };

    private void ChoosingMatch(bool choosing) => _lobby?.SetChoosingMap(choosing);

    /// <summary>The other machine has the same maps and goes looking for this one by its file, and plays by the rules it is sent with it.</summary>
    private void AnnounceFight(MapDefinition map) => _lobby?.StartFight(map.FileName, setup.Rules);

    private void SwitchCorners()
    {
        if (_lobby is { PeerPresent: true, LocalReady: false } lobby) lobby.RequestSide(1 - lobby.LocalSide);
    }

    /// <summary>Who the other machine has picked, by their id, null when there is nobody there or this isn't online.</summary>
    private string? OpponentCharacter => _lobby is { PeerPresent: true } lobby && lobby.RemoteCharacter.Length > 0 ? lobby.RemoteCharacter : null;

    /// <summary>
    /// What the line under a player's gamepad says while they haven't locked in.
    /// </summary>
    private string DescribeWaiting(Gamepad gamepad)
    {
        // Nobody gets to be ready before the files of the fight are here
        if (_lobby is { IsHost: false } lobby && lobby.Content.Phase != ContentPhase.Ready)
        {
            return lobby.Content.Phase switch
            {
                ContentPhase.Downloading => $"getting the game files {lobby.Content.Progress * 100:0}%",
                ContentPhase.Failed => $"no fight, {lobby.Content.Error}",
                _ => "checking the game files"
            };
        }

        return ButtonGlyphs.Localize(_lobby is null ? "[icon:pad_a] lock in    [icon:pad_y] bindings" : "[icon:pad_a] ready    [icon:pad_y] bindings", gamepad);
    }

    private void ShowOpponent(FighterPanel panel)
    {
        if (_lobby is not { } lobby) return;

        panel.ShowTitle("Your opponent", lobby.LocalSide == 0 ? RedCorner : BlueCorner);

        if (!lobby.PeerPresent)
        {
            // Only the host ever sees this, somebody who joined has the host for company
            panel.Show(null);
            panel.ShowStatus("waiting for a player", _slowPulse.Grey);
            panel.ShowDetail("to join", MenuColors.Hint);
            return;
        }

        panel.Show(_characters.Find(character => character.Id == lobby.RemoteCharacter));
        panel.ShowStatus("on the other machine", Vector4.One);
        panel.ShowDetail(lobby.RemoteReady ? "ready!" : "making up their mind", lobby.RemoteReady ? MenuColors.Ready : MenuColors.Hint);

        // The host sees how far along the other player is with getting its files
        if (lobby.IsHost && !lobby.Content.PeerVerified)
        {
            panel.ShowDetail(lobby.Content.IsTransferring
                ? $"sending them the game files {lobby.Content.Progress * 100:0}%"
                : "checking their game files", MenuColors.Hint);
        }
    }

    /// <summary>
    /// What the hint says in a lobby, null when it has nothing to add to what the screen says anyway.
    /// </summary>
    private string? LobbyHint
    {
        get
        {
            if (_lobby is not { } lobby || setup.Slots.Count == 0) return null;

            if (!lobby.PeerPresent) return $"They can find you at {_hostAddresses}    {HINT_LOBBY_ALONE}";

            if (lobby.BothReady)
            {
                if (lobby.IsHost) return null;
                return lobby.ChoosingMap ? "The host is settling on the map and the rules    [icon:pad_b] not ready" : "Waiting for the host    [icon:pad_b] not ready";
            }

            return lobby.LocalReady ? "waiting for them to be ready    [icon:pad_b] not ready" : HINT_LOBBY;
        }
    }
}
