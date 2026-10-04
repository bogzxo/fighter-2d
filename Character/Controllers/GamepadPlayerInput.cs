using Fighter2D.Logic;
using Horizon.Engine;
using Silk.NET.Input;
using System.Linq;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Input for players driven by a gamepad, all it does is read the buttons.
/// </summary>
internal class GamepadPlayerInput : IPlayerInput
{
    public string Name => "Player";
    public bool IsConnected => _gamepad is { IsConnected: true };
    private readonly IGamepad? _gamepad;

    public GamepadPlayerInput(int index)
    {
        _gamepad = GameEngine.Instance.InputManager.NativeInputContext?.Gamepads.ElementAtOrDefault(index);
    }

    public InputFlags Read()
    {
        // An unplugged gamepad simply holds nothing
        if (_gamepad is not { IsConnected: true }) return InputFlags.None;

        return (
            Flag(_gamepad.DPadLeft(), InputFlags.DPadLeft) |
            Flag(_gamepad.DPadRight(), InputFlags.DPadRight) |
            Flag(_gamepad.DPadUp(), InputFlags.DPadUp) |
            Flag(_gamepad.DPadDown(), InputFlags.DPadDown) |
            Flag(_gamepad.A(), InputFlags.A) |
            Flag(_gamepad.B(), InputFlags.B) |
            Flag(_gamepad.X(), InputFlags.X) |
            Flag(_gamepad.Y(), InputFlags.Y) |
            Flag(_gamepad.RightBumper(), InputFlags.RightBumper)
        );
    }

    private static InputFlags Flag(Button button, InputFlags flag) => button.Pressed ? flag : InputFlags.None;
}
