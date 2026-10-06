using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.Match;
using Fighter2D.Networking;
using Fighter2D.Scenes;

namespace Fighter2D;

internal enum MatchMode
{
    // One player against the dummy
    Practice,

    // Two players on two gamepads
    Pvp,

    // Two players on two machines, this one is where the other one connects to
    Host,

    // Two players on two machines, this one connected to the other
    Join
}

/// <summary>
/// What the menus have settled on so far for the next fight, handed from one screen to the next.
/// </summary>
internal class MatchSetup(MatchMode mode)
{
    // How far apart the two players start, in the units of the world
    private const float SPAWN_GAP = 256;

    public MatchMode Mode { get; } = mode;

    public bool IsOnline => Mode is MatchMode.Host or MatchMode.Join;

    // How many players there are on this machine, each of them needs a gamepad
    public int PlayerCount => Mode == MatchMode.Pvp ? 2 : 1;

    // The slot of the gamepad every player has picked, player one first
    public List<int> Slots { get; } = [];

    public bool IsReady => Slots.Count >= PlayerCount;

    // What the match is played by, settled on after the map is picked (MatchRulesScene) and kept for the next match
    public MatchRules Rules { get; } = new();

    // The character every player of this machine has picked, by its name in the content
    private readonly List<string> _characters = [];

    /// <summary>
    /// The character a player picked, empty for somebody who hasn't (they get the first one there is).
    /// </summary>
    public string GetCharacter(int player) => player < _characters.Count ? _characters[player] : string.Empty;

    public void SetCharacter(int player, string id)
    {
        while (_characters.Count <= player) _characters.Add(string.Empty);
        _characters[player] = id;

        // The other machine has to know who it is up against
        if (player == 0) Lobby?.SetCharacter(id);
    }

    // The connection to the other machine and what has been agreed on over it, only there for an online fight
    public NetSession? Session { get; private set; }
    public OnlineLobby? Lobby { get; private set; }

    public string Title => Mode switch
    {
        MatchMode.Pvp => "Player vs Player",
        MatchMode.Host => "Host Server",
        MatchMode.Join => "Multiplayer",
        _ => "Practice"
    };

    /// <summary>
    /// Helper method to make the setup of an online fight out of a session that is up and running.
    /// </summary>
    public static MatchSetup Online(NetSession session)
    {
        return new MatchSetup(session.IsHost ? MatchMode.Host : MatchMode.Join)
        {
            Session = session,
            Lobby = new OnlineLobby(session)
        };
    }

    /// <summary>
    /// Called when the players part ways before a fight, hangs up on the other machine if there is one.
    /// </summary>
    public void Close()
    {
        Session?.Dispose();
    }

    /// <summary>
    /// Helper method to make the fight everything was set up for.
    /// </summary>
    public FightScene CreateFight(MapLoader.MapDefinition mapDefinition)
    {
        int slot = Slots.Count > 0 ? Slots[0] : 0;

        // Where the players start going by maps.hor. A map that has spawns of its own in it puts them there instead
        // once it is loaded, see FightScene
        Vector2 left = new(mapDefinition.SpawnPosition.X * 16, MapLoader.SPAWN_BASE_ROW * 16 - mapDefinition.SpawnPosition.Y * 16);
        Vector2 right = left + new Vector2(SPAWN_GAP, 0);

        if (Lobby is not null)
        {
            // The other player is played on their machine, all we see of them is what it tells us
            bool onTheRight = Lobby.LocalSide == 1;

            return new FightScene(mapDefinition, slot, new NetworkPlayer(Lobby.IsHost ? "joined us" : "the host")
            {
                SpawnPosition = onTheRight ? left : right,
                CharacterId = Lobby.RemoteCharacter,
            })
            {
                Session = Session,
                SpawnOffset = onTheRight ? SPAWN_GAP : 0,
                CharacterId = GetCharacter(0),

                // Both machines play by what the host settled on, which came along with the map
                Rules = Lobby.StartedRules ?? Rules
            };
        }

        if (Mode == MatchMode.Pvp && Slots.Count > 1)
        {
            // Player two is just another local player on the second gamepad
            return new FightScene(mapDefinition, slot, new Player()
            {
                Controller = new LocalPlayerController(new GamepadPlayerInput(Slots[1])),
                SpawnPosition = right,
                CharacterId = GetCharacter(1),
            })
            {
                CharacterId = GetCharacter(0),
                Rules = Rules
            };
        }

        return new FightScene(mapDefinition, slot) { CharacterId = GetCharacter(0), Rules = Rules };
    }
}
