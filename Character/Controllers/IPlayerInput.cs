using Fighter2D.Logic;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Where the buttons of a <see cref="LocalPlayerController"/> come from (a gamepad, the AI etc.)
/// An input only says which buttons are held down, the matching of them to moves is the same for everyone.
/// </summary>
internal interface IPlayerInput
{
    // The title of the debug window, has to be different for every player on screen
    string Name { get; }

    // How fast the player walks compared to PlayerConfig.WALK_SPEED, slower opponents are easier to get away from
    float WalkSpeedScale => 1.0f;

    /// <summary>
    /// Called once the controller is ready, for inputs that need to look at the fight (the AI)
    /// </summary>
    void Attach(PlayerController controller) { }

    /// <summary>
    /// Returns every button that is held down right now, called once for every input tick.
    /// </summary>
    InputFlags Read();

    /// <summary>
    /// Called the moment the other player starts an attack.
    /// </summary>
    void OnOpponentAttack() { }
}
