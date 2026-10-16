using Fighter2D.Tests.Support;

namespace Fighter2D.Tests.Simulation;

/// <summary>
/// The combat rules out of Assets/data/combat_rules.hor, the one place the numbers are.
/// </summary>
public class RulesTests
{
    [Fact]
    public void TheRulesFileHasEveryRule()
    {
        // Making a fight reads the content, the rules among it, and throws on a missing one
        _ = new TestFight();

        Assert.Equal(9, CombatRules.STALE_STEPS.Length);
        Assert.True(CombatRules.MAX_HITLAG > CombatRules.HITLAG_BASE);
        Assert.True(CombatRules.PARRY_TICKS > 0 && CombatRules.TECH_WINDOW > 0);
        Assert.True(CombatRules.Movement.Gravity > 0 && CombatRules.Movement.JumpHeight > 0);
    }

    [Fact]
    public void ACharacterWithoutAMovementBlockMovesByTheRules()
    {
        _ = new TestFight();
        var movement = Fighter2D.Data.CharacterMovement.Read([], out string? warning);

        Assert.Null(warning);
        Assert.Equal(CombatRules.Movement, movement);
    }
}
