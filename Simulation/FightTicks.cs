namespace Fighter2D.Simulation;

internal static class FightTicks
{
    // How a character moves (walk speed, the impulse of its jump and its roll) is up to its definition and its move files

    // How many ticks a second the fight runs at. Inputs, moves, hitstun and hits are all counted in these.
    // Animations play at the frame rate of their character on top of that
    public const float TICK_RATE = 60f;
    public const float TICK_TIME = 1.0f / TICK_RATE;

    // How long a press stays good for, the gap of a double tap and the window of a two button move are rules of the
    // game, in Assets/data/combat_rules.hor (CombatRules.INPUT_BUFFER_TICKS and the two after it)
}
