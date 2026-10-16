namespace Fighter2D.Simulation;

/// <summary>
/// What became of an attack once its hit came out.
/// </summary>
internal enum HitResult
{
    // Hit nothing but air, or went straight through i-frames (dodge roll)
    Whiff,

    // They were blocking and facing it
    Blocked,

    Hit,

    // Caught them in the startup of their own attack, which hurts more
    CounterHit,

    // Caught them in the recovery of an attack they already threw
    Punish,

    // They put their block up just as it came in, and the attacker is the one stuck
    Parried
}
