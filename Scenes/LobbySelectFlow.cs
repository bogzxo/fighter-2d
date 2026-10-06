using System;
using System.Numerics;

using Fighter2D.Map;
using Fighter2D.Match;
using Fighter2D.Networking;

using Horizon.Input2;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.Scenes;

/// <summary>
/// The gamepad select screen of an online fight, which doubles as its lobby. The two cards are the two corners with whoever is standing in each.
/// Both players pick a corner and say they are ready, then the host goes off to choose the map while the other player waits right here.
/// </summary>
internal sealed class LobbySelectFlow : GamepadSelectFlow
{
    private const string HINT_CORNER = "[icon:dpad] switch corners    [icon:pad_a] ready";
    private const string HINT_HOST_READY = "[icon:pad_a] choose the map    [icon:pad_b] not ready";
    private const string HINT_SETUP = "[icon:pad_x] fighter    [icon:pad_y] bindings    [icon:pad_b] back";

    private readonly OnlineLobby _lobby;
    private readonly string _hostAddresses = NetSession.DescribeLocalAddresses();

    // The lobby as it was when we last looked, so the cards can jump when something about it changes
    private int _revision;
    private bool _hadPeer;

    public LobbySelectFlow(GamepadSelectorScene scene, MatchSetup setup, OnlineLobby lobby, PlayerCard[] cards, Label hint)
        : base(scene, setup, cards, hint)
    {
        _lobby = lobby;

        // The host answers with how things stand (and is back from choosing the map, if that is where we come from)
        lobby.SayHello();
        lobby.SetChoosingMap(false);
        lobby.SetCharacter(setup.GetCharacter(0));

        _revision = lobby.Revision;
        _hadPeer = lobby.PeerPresent;
    }

    // Our card is whichever corner we are standing in
    public override int NextCard => _lobby.LocalSide;

    private PlayerCard OurCard => Cards[_lobby.LocalSide];
    private PlayerCard TheirCard => Cards[1 - _lobby.LocalSide];

    public override bool Update()
    {
        if (_lobby.IsClosed)
        {
            // The host is gone and the lobby with them
            Scene.Back();
            return true;
        }

        if (_lobby.StartedMap is { } mapFile)
        {
            FollowHostIntoFight(mapFile);
            return true;
        }

        // Everybody notices when somebody walks in (or out), and when the corners or who is ready change
        if (_lobby.PeerPresent != _hadPeer)
        {
            _hadPeer = _lobby.PeerPresent;
            TheirCard.Bump();
        }
        else if (_lobby.Revision != _revision)
        {
            foreach (PlayerCard card in Cards) card.Bump(0.05f, 0.25f);
        }

        _revision = _lobby.Revision;
        return false;
    }

    private void FollowHostIntoFight(string mapFile)
    {
        if (!MapLoader.TryFind(mapFile, out MapDefinition map))
        {
            Console.WriteLine($"[Lobby] The host started on '{mapFile}', which is not a map we have.");
            Scene.Back();
            return;
        }

        Scene.GoTo(Setup.CreateFight(map));
    }

    public override bool HandlePicked(Gamepad gamepad)
    {
        if (gamepad.WasPressed(GamepadInput.B))
        {
            // One step back at a time. Not ready any more first, then the gamepad goes back to being anybody's
            if (_lobby.LocalReady) _lobby.SetReady(false);
            else Setup.Slots.Clear();

            return true;
        }

        if (gamepad.WasPressed(GamepadInput.X) && !_lobby.LocalReady)
        {
            // Who we play is ours to pick whenever we like, as long as we haven't said we are ready
            Scene.GoTo(new CharacterSelectScene(Setup));
            return true;
        }

        // Corners and being ready are between two players, there is nothing to settle while we are alone
        if (!_lobby.PeerPresent) return false;

        int side = MenuInput.Horizontal(gamepad);
        if (side != 0)
        {
            _lobby.RequestSide(side < 0 ? 0 : 1);
            return true;
        }

        if (!gamepad.WasPressed(GamepadInput.A)) return false;

        if (!_lobby.LocalReady)
        {
            _lobby.SetReady(true);
        }
        else if (_lobby.IsHost && _lobby.BothReady)
        {
            // The map is the host's to choose, the other player waits right here
            _lobby.SetChoosingMap(true);
            Scene.GoTo(new MapSelectionScene(Setup));
        }

        return true;
    }

    public override void Refresh()
    {
        Gamepad? gamepad = null;
        if (Setup.Slots.Count > 0) GameInput.Manager.TryGet(Setup.Slots[0], out gamepad);

        RefreshOurCard(gamepad);
        RefreshTheirCard();

        if (!_lobby.PeerPresent)
        {
            Hint.Text = $"They can find you at {_hostAddresses}";
        }
        else if (_lobby.BothReady)
        {
            Hint.Text = _lobby.IsHost
                ? ButtonGlyphs.Localize(HINT_HOST_READY, gamepad)
                : _lobby.ChoosingMap ? "The host is choosing the map" : "Waiting for the host to choose the map";
        }
        else
        {
            Hint.Text = ButtonGlyphs.Localize(HINT_SETUP, gamepad);
        }
    }

    private void RefreshOurCard(Gamepad? gamepad)
    {
        if (gamepad is null)
        {
            ShowWaitingForButton(OurCard);
            return;
        }

        OurCard.ShowStatus($"you - {PlayerCard.Shorten(gamepad.Name)}", gamepad.AnyDown ? Vector4.One : MenuColors.Name);
        ShowReady(OurCard, _lobby.LocalReady, _lobby.PeerPresent ? ButtonGlyphs.Localize(HINT_CORNER, gamepad) : string.Empty);

        // Nobody gets to be ready before the files of the fight are here
        ContentSync content = _lobby.Content;
        if (!_lobby.IsHost && content.Phase != ContentPhase.Ready)
        {
            OurCard.ShowDetail(DescribeDownload(content), content.Phase == ContentPhase.Failed ? MenuColors.Error : MenuColors.Hint);
        }
    }

    private void RefreshTheirCard()
    {
        if (!_lobby.PeerPresent)
        {
            // Only the host ever sees this, somebody who joined has the host for company
            TheirCard.ShowStatus("waiting for a player", Scene.SlowPulse.Grey);
            TheirCard.ShowDetail("to join", MenuColors.Hint);
            return;
        }

        TheirCard.ShowStatus("your opponent", Vector4.One);
        ShowReady(TheirCard, _lobby.RemoteReady, "making up their mind");

        // The host sees how far along the other player is with getting its files
        if (_lobby.IsHost && !_lobby.Content.PeerVerified)
        {
            string progress = _lobby.Content.IsTransferring
                ? $"sending them the game files {_lobby.Content.Progress * 100:0}%"
                : "checking their game files";

            TheirCard.ShowDetail(progress, MenuColors.Hint);
        }
    }

    private static void ShowReady(PlayerCard card, bool ready, string otherwise)
    {
        card.ShowDetail(ready ? "ready!" : otherwise, ready ? MenuColors.Ready : MenuColors.Hint);
    }

    /// <summary>
    /// Helper method to say how we are doing with getting the files of the host.
    /// </summary>
    private static string DescribeDownload(ContentSync content) => content.Phase switch
    {
        ContentPhase.Downloading => $"getting the game files {content.Progress * 100:0}%",
        ContentPhase.Failed => $"no fight: {content.Error}",
        _ => "checking the game files"
    };
}
