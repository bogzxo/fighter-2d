namespace Fighter2D.Networking;

/// <summary>
/// Everything the two machines of an online fight say to each other.
/// </summary>
internal enum NetMessage : ushort
{
    // Lobby
    LobbyHello = 1,
    LobbyInput,
    LobbyState,
    StartFight,

    // Making sure both machines have the same content
    ContentEntry,
    ContentRequest,
    ContentChunk,
    ContentReady,

    // Fight
    PlayerState,
    Hit,
    RoundState
}
