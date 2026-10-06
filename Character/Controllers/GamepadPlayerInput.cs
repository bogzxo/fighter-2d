using Fighter2D.Logic;

using Horizon.Input2;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Input for players driven by a gamepad, all it does is read what the gamepad in a slot is holding.
/// Which button does what is up to the bindings of that gamepad, see <see cref="GameInput"/>.
/// </summary>
internal class GamepadPlayerInput(int slot) : IPlayerInput
{
    public Gamepad? Gamepad => GameInput.Manager.TryGet(slot, out Gamepad gamepad) ? gamepad : null;

    public InputFlags Read()
    {
        // An unplugged gamepad simply holds nothing, and carries on once it is plugged back in
        if (!GameInput.Manager.TryGet(slot, out Gamepad gamepad)) return InputFlags.None;

        return GameInput.ReadFight(gamepad);
    }
}
