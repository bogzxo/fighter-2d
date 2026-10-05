using Fighter2D.Logic;
using Horizon.Input2;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Input for players driven by a gamepad, all it does is read what the gamepad in a slot holds.
/// Which button is which action is up to the bindings of that gamepad, see <see cref="GameInput"/>.
/// </summary>
internal class GamepadPlayerInput(int slot) : IPlayerInput
{
    public string Name => $"Player {slot + 1}";
    public bool IsConnected => GameInput.Manager.TryGet(slot, out Gamepad gamepad) && gamepad.IsConnected;

    public InputFlags Read()
    {
        // An unplugged gamepad simply holds nothing, and carries on once it is back in its slot
        if (!GameInput.Manager.TryGet(slot, out Gamepad gamepad)) return InputFlags.None;

        return GameInput.ReadFight(gamepad);
    }
}
