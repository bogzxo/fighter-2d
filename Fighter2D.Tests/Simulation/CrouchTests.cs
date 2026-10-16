using Fighter2D.Tests.Support;

namespace Fighter2D.Tests.Simulation;

/// <summary>
/// Holding down is the crouch, and what comes of pressing things while down there.
/// </summary>
public class CrouchTests
{
    [Fact]
    public void HoldingDownDucks()
    {
        var fight = new TestFight();
        fight.OneInput.Hold(5, 40, InputFlags.DPadDown);

        fight.Run(40);

        Assert.Equal(5, fight.StartOf("crouch"));
        Assert.Equal(Stance.Crouching, fight.One.Controller.State.CurrentStance);
    }

    [Fact]
    public void ACrouchingAttackThatLandsDoesNotDuckAgain()
    {
        // The low kick lands, down is still held, and what follows is holding the crouch, not the duck all over again
        var fight = new TestFight(apart: 40.0f);
        fight.OneInput.Hold(5, 80, InputFlags.DPadDown).Tap(5, InputFlags.A);

        fight.Run(80);

        Assert.Equal(5, fight.StartOf("low_kick"));
        Assert.True(fight.Two.Health < CombatRules.MAX_HEALTH, "the low kick should have landed");
        Assert.Equal(-1, fight.StartOf("crouch"));
        Assert.True(fight.StartOf("crouch_hold") > 5, "down held after the kick is the crouch held, not the duck");
    }

    [Fact]
    public void DownAndBlockIsTheCrouchingBlock()
    {
        var fight = new TestFight();
        fight.OneInput.Hold(5, 30, InputFlags.DPadDown | InputFlags.RightBumper);

        fight.Run(30);

        Assert.Equal(5, fight.StartOf("crouch_block"));
        Assert.Equal(-1, fight.StartOf("block"));
    }

    [Fact]
    public void AnotherButtonWhileCrouchingStandsUpForItsStandingMove()
    {
        // Already down there, the shove has no crouching version, so it comes out standing rather than not at all
        var fight = new TestFight();
        fight.OneInput.Hold(5, 40, InputFlags.DPadDown).Tap(20, InputFlags.LeftTrigger);

        fight.Run(40);

        Assert.Equal(5, fight.StartOf("crouch"));
        Assert.Equal(20, fight.StartOf("shove"));
    }
}
