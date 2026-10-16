
namespace Fighter2D.Fighters;

/// <summary>
/// A player who sits at another machine. They are played like anybody else, just with the buttons their machine says they pressed.
/// FightNetwork feeds those buttons in and fixes things up whenever our copy drifts away from theirs.
/// </summary>
internal class NetworkPlayer : Player
{
    /// <summary>
    /// The buttons of the other machine, FightNetwork hands them over as they come in.
    /// </summary>
    public NetworkPlayerInput Input { get; } = new();

    public NetworkPlayer()
    {
        Controller = new PlayerController(Input);
    }
}
