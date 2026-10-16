using Horizon.Content;
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

    // The rules of the match, picked on the fight setup screen (FightSetupScene) and kept for the rematch
    public MatchRules Rules { get; } = new();

    // The character every player of this machine has picked, by its id in the content
    private readonly List<string> _characters = [];

    // The character the dummy is in a practice fight, by its id in the content. Empty for the first one there is.
    // The training menu changes it mid fight, and it stays changed for the rematch
    public string DummyCharacter { get; set; } = string.Empty;

    /// <summary>
    /// Where the two players stand at the start of every round, in units of the world across the stage, each facing
    /// the other. Null for the spawn of the map (both on it, pushed apart by their bodies, which lands them somewhere a
    /// little different every time). For tests that have to start from the same place every time (--positions).
    /// </summary>
    public (float One, float Two)? TestPositions { get; set; }

    /// <summary>
    /// What the dummy does from the very first tick, rather than fighting until somebody changes it in the pause menu
    /// (by which time it has dashed off somewhere). For tests (--dummy stand). Null for the usual, it fights.
    /// </summary>
    public DummyMode? StartingDummyMode { get; set; }

    // The connection to the other machine and what has been agreed on over it, only there for an online fight
    public NetSession? Session { get; private set; }
    public OnlineLobby? Lobby { get; private set; }

    public string Title => Mode switch
    {
        MatchMode.Pvp => Localisation.Get("modes.pvp"),
        MatchMode.Host => Localisation.Get("modes.host"),
        MatchMode.Join => Localisation.Get("modes.join"),
        _ => Localisation.Get("modes.practice")
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
    /// Helper method to make the fight everything was set up for, the one way to make one. The scene reads everything else off
    /// this setup, and comes back here for the rematch and for a reload. Where the players spawn is up to the scene, it knows the map.
    /// </summary>
    /// <param name="resume">Where a fight that is being built again was, null for one that starts from the top.</param>
    public FightScene CreateFight(MapDefinition mapDefinition, FightResume? resume = null)
    {
        return new FightScene(this, mapDefinition, resume);
    }

    /// <summary>
    /// Helper method to make player two, null leaves it to the scene which puts the dummy there.
    /// </summary>
    public Player? CreateOpponent()
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
