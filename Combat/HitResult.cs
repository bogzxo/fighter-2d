namespace Fighter2D.Combat;

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
    Punish
}
