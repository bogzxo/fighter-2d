using Fighter2D.Character.Controllers;

namespace Fighter2D.Character;

/// <summary>
/// Representation of a remote player connected via Riptide network. It is played like any other player, by the buttons
/// the other machine says were pressed, and put right by FightNetwork wherever that comes apart from how they are doing over there.
/// </summary>
internal class NetworkPlayer : Player
{
    public readonly string Address;

    /// <summary>
    /// The buttons of the other machine, FightNetwork hands them over as they come in.
    /// </summary>
    public NetworkPlayerInput Input { get; } = new();

    public NetworkPlayer(string address)
    {
        this.Address = address;
        Controller = new PlayerController(Input);
    }
}
