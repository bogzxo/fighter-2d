namespace Fighter2D.Character.Controllers;

internal static class PlayerConfig
{
    public const float JUMP_IMPULSE = 3500f;  // Changed from force to impulse
    public const float WALK_SPEED = 2000f;        // Desired max speed
    public const float DASH_MULTIPLIER = 1.75f;   // Dash speed multiplier

    public const float INPUT_TICK_RATE = 60f;     // How many times a second the inputs are read
    public const int INPUT_BUFFER_TICKS = 8;      // How long a press stays valid (in frames), so pressing slightly too early still counts
    public const int DOUBLE_TAP_TICKS = 18;       // Longest gap (in frames) between the two presses of a double tap
}
