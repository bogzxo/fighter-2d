namespace Fighter2D.Networking;

/// <summary>
/// Everything the two machines of an online fight say to each other.
/// </summary>
internal enum NetMessage : ushort
{
    // Lobby (see OnlineLobby)
    LobbyHello = 1,
    LobbyInput,
    LobbyState,
    StartFight,

    // Making sure both machines have the same content (see ContentSync)
    ContentEntry,
    ContentRequest,
    ContentChunk,
    ContentReady,

    // Fight (see FightNetwork)
    PlayerInput,
    PlayerState,
    Hit,
    Whiff,
    RoundState,
    Pause,
    Reload,
    ReloadCheck,
    Reloaded
}
