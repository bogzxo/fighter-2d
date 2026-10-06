namespace Fighter2D.Character.Controllers;

internal static class PlayerConfig
{
    // How a character moves (walk speed, the impulse of its jump and its roll) is up to its definition and its move files

    // How many ticks a second the fight runs at. Inputs, moves, hitstun and hits are all counted in these.
    // Animations play at the frame rate of their character on top of that
    public const float TICK_RATE = 60f;
    public const float TICK_TIME = 1.0f / TICK_RATE;

    public const int INPUT_BUFFER_TICKS = 8;      // How long a press stays valid (in ticks), so pressing slightly too early still counts
    public const int DOUBLE_TAP_TICKS = 18;       // Longest gap (in ticks) between the two presses of a double tap
}
