using Fighter2D.Logic;

using Horizon.Input2;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Where the buttons of a <see cref="PlayerController"/> come from (a gamepad, the dummy, another machine etc.)
/// An input only says which buttons are held down, matching them to moves is the same for everybody.
/// </summary>
internal interface IPlayerInput
{
    // How fast the player walks compared to the walk speed of their character, a slow opponent is easier to get away from
    float WalkSpeedScale => 1.0f;

    // The gamepad the buttons come from, so the HUD can show the right button icons. Null for anybody who isn't holding one
    Gamepad? Gamepad => null;

    // Whether the buttons are pressed on another machine, in which case that machine also decides what happens to the player
    bool IsRemote => false;

    /// <summary>
    /// Called once the controller is ready, for inputs that need to look at the fight (the dummy)
    /// </summary>
    void Attach(PlayerController controller) { }

    /// <summary>
    /// Returns every button that is held down right now, called once for every tick of the fight.
    /// </summary>
    InputFlags Read();

    /// <summary>
    /// Called the moment the other player starts an attack.
    /// </summary>
    void OnOpponentAttack() { }
}
