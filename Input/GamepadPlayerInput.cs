
using Horizon.Input;

namespace Fighter2D.Input;

/// <summary>
/// Input for players driven by a gamepad, all it does is read what the gamepad in a slot is holding.
/// Which button does what is up to the bindings of that gamepad, see <see cref="GameInput"/>.
/// </summary>
internal class GamepadPlayerInput(int slot) : IPlayerInput
{
    public Gamepad? Gamepad => GameInput.Manager.TryGet(slot, out Gamepad gamepad) ? gamepad : null;

    /// <summary>
    /// Asked every tick, true and the fighter holds nothing whatever the gamepad does. The lab of a practice fight has the
    /// gamepad while its drawer is out (see TrainingDrawer).
    /// </summary>
    public Func<bool>? Muted { get; init; }

    public InputFlags Read()
    {
        // An unplugged gamepad simply holds nothing, and carries on once it is plugged back in
        if (!GameInput.Manager.TryGet(slot, out Gamepad gamepad) || Muted?.Invoke() == true) return InputFlags.None;

        return GameInput.ReadFight(gamepad);
    }
}
