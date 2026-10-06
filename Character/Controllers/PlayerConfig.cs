namespace Fighter2D.Character.Controllers;

internal static class PlayerConfig
{
    // How a character moves (walk speed, the impulse of its jump and its roll) is up to its definition and its move files
    public const float INPUT_TICK_RATE = 60f;     // How many times a second the inputs are read
    public const int INPUT_BUFFER_TICKS = 8;      // How long a press stays valid (in frames), so pressing slightly too early still counts
    public const int DOUBLE_TAP_TICKS = 18;       // Longest gap (in frames) between the two presses of a double tap
}
