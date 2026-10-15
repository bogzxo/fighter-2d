namespace Fighter2D.Fighters.Control;

internal static class FightTicks
{
    // How a character moves (walk speed, the impulse of its jump and its roll) is up to its definition and its move files

    // How many ticks a second the fight runs at. Inputs, moves, hitstun and hits are all counted in these.
    // Animations play at the frame rate of their character on top of that
    public const float TICK_RATE = 60f;
    public const float TICK_TIME = 1.0f / TICK_RATE;

    public const int INPUT_BUFFER_TICKS = 8;      // How long a press stays valid (in ticks), so pressing slightly too early still counts
    public const int DOUBLE_TAP_TICKS = 18;       // Longest gap (in ticks) between the two presses of a double tap

    // How long (in ticks) after a press started a move the second button of a two button move still turns it into that one.
    // Nobody presses A and B on the same sixtieth of a second, the A comes first and has started a kick by the time B is down
    public const int CHORD_TICKS = 4;
}
