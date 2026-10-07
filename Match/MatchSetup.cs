using System.Collections.Generic;

namespace Fighter2D.Match;

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
/// Everything the menus have settled on so far for the next fight, handed from one screen to the next.
/// </summary>
internal class MatchSetup(MatchMode mode)
{
    public MatchMode Mode { get; } = mode;

    public bool IsOnline => Mode is MatchMode.Host or MatchMode.Join;

    // How many players there are on this machine, each of them needs a gamepad
    public int PlayerCount => Mode == MatchMode.Pvp ? 2 : 1;

    // The slot of the gamepad every player has picked, player one first
    public List<int> Slots { get; } = [];

    public bool IsReady => Slots.Count >= PlayerCount;

    // The rules of the match, picked after the map (MatchRulesScene) and kept for the next match
    public MatchRules Rules { get; } = new();

    // The character every player of this machine has picked, by its id in the content
    private readonly List<string> _characters = [];

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
    /// The slot of the gamepad that drives the menus, which is player one's.
    /// </summary>
    public int MenuSlot => Slots.Count > 0 ? Slots[0] : 0;

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
    /// Helper method to make the fight everything was set up for. Where the players spawn is up to the scene, it knows the map.
    /// </summary>
    /// <param name="resume">Where a fight that is being built again was, null for one that starts from the top.</param>
    public FightScene CreateFight(MapDefinition mapDefinition, FightResume? resume = null)
    {
        return new FightScene(mapDefinition, MenuSlot, CreateOpponent())
        {
            Session = Session,
            Content = Lobby?.Content,
            Resume = resume,

            // The same fight from where it was, with everything read from disk again. The map as well, by the file it is in
            Reload = from => CreateFight(MapLoader.TryFind(mapDefinition.FileName, out MapDefinition fresh) ? fresh : mapDefinition, from),

            StartsOnTheRight = Lobby is { LocalSide: 1 },
            CharacterId = GetCharacter(0),

            // Online both machines play by what the host settled on, which came along with the map
            Rules = Lobby?.StartedRules ?? Rules,

            // The same fight all over again. Not online, the other machine would have to want that as well
            Rematch = IsOnline ? null : () => CreateFight(mapDefinition)
        };
    }

    /// <summary>
    /// Helper method to make player two, null leaves it to the scene which puts the dummy there.
    /// </summary>
    private Player? CreateOpponent()
    {
        if (Lobby is not null)
        {
            // The other player is played on their machine, all we see of them is what it tells us
            return new NetworkPlayer() { CharacterId = Lobby.RemoteCharacter };
        }

        if (Mode == MatchMode.Pvp && Slots.Count > 1)
        {
            // Player two is just another local player on the second gamepad
            return new Player()
            {
                Controller = new PlayerController(new GamepadPlayerInput(Slots[1])),
                CharacterId = GetCharacter(1)
            };
        }

        return null;
    }
}
