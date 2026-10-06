using Horizon.Input2;

namespace Fighter2D;

/// <summary>
/// The directions of the menus, on the d-pad or the left stick. Menus can't be rebound, otherwise a bad binding could lock somebody out of the screen that fixes it.
/// Everything in here is only true on the update the direction was pushed, so a menu moves one entry per push.
/// </summary>
internal static class MenuInput
{
    public static bool UpPressed(Gamepad gamepad) => gamepad.WasPressed(GamepadInput.DPadUp) || gamepad.WasPressed(GamepadInput.LeftStickUp);
    public static bool DownPressed(Gamepad gamepad) => gamepad.WasPressed(GamepadInput.DPadDown) || gamepad.WasPressed(GamepadInput.LeftStickDown);
    public static bool LeftPressed(Gamepad gamepad) => gamepad.WasPressed(GamepadInput.DPadLeft) || gamepad.WasPressed(GamepadInput.LeftStickLeft);
    public static bool RightPressed(Gamepad gamepad) => gamepad.WasPressed(GamepadInput.DPadRight) || gamepad.WasPressed(GamepadInput.LeftStickRight);

    /// <summary>
    /// 1 if right was pushed, -1 if left was and 0 if neither.
    /// </summary>
    public static int Horizontal(Gamepad gamepad) => (RightPressed(gamepad) ? 1 : 0) - (LeftPressed(gamepad) ? 1 : 0);

    /// <summary>
    /// 1 if down was pushed, -1 if up was and 0 if neither. Down is positive because lists count downwards.
    /// </summary>
    public static int Vertical(Gamepad gamepad) => (DownPressed(gamepad) ? 1 : 0) - (UpPressed(gamepad) ? 1 : 0);

    /// <summary>
    /// Whether the button that confirms things was pressed, which is A or start.
    /// </summary>
    public static bool ConfirmPressed(Gamepad gamepad) => gamepad.WasPressed(GamepadInput.A) || gamepad.WasPressed(GamepadInput.Start);
}
