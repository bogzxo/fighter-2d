namespace CumInstinctDuel.Logic;

/// <summary>
/// Defines high-priority status conditions that override normal player control 
/// (e.g., Stunned, ComboTrapped, KnockedDown).
/// </summary>
public enum PlayerStatusType
{
    Normal,
    Stunned,
    ComboTrapped,
    KnockedDown
}