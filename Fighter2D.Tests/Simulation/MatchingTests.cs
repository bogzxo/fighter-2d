using Fighter2D.Tests.Support;

namespace Fighter2D.Tests.Simulation;

/// <summary>
/// Which move a press comes out as. The direction held with the button picks the stance, nobody ducks first.
/// </summary>
public class MatchingTests
{
    [Fact]
    public void DownAndKickIsTheLowKickFromStanding()
    {
        var fight = new TestFight();
        fight.OneInput.Hold(5, 10, InputFlags.DPadDown).Tap(5, InputFlags.A);

        fight.Run(12);

        Assert.Equal(5, fight.StartOf("low_kick"));
        Assert.Equal(-1, fight.StartOf("crouch"));
    }

    [Fact]
    public void BlockysLeftBumperIsTheHook()
    {
        // The roll was on it as well, first in line, and the hook never came out
        var fight = new TestFight();
        fight.OneInput.Tap(5, InputFlags.LeftBumper);

        fight.Run(12);

        Assert.Equal(5, fight.StartOf("hook"));
        Assert.Equal(-1, fight.StartOf("dodge_roll"));
    }

    [Fact]
    public void BlockyRollsOnADoubleTap()
    {
        var fight = new TestFight();
        fight.OneInput.Tap(5, InputFlags.DPadRight).Tap(12, InputFlags.DPadRight);

        fight.Run(20);

        Assert.Equal(12, fight.StartOf("dodge_roll"));
    }

    [Fact]
    public void KickAloneIsTheStandingKick()
    {
        var fight = new TestFight();
        fight.OneInput.Tap(5, InputFlags.A);

        fight.Run(12);

        Assert.Equal(5, fight.StartOf("kick_left"));
    }

    [Fact]
    public void DownAndAButtonWithNoCrouchingVersionComesOutStanding()
    {
        // The shove is a standing move and nothing else, down held with it is still the shove
        var fight = new TestFight();
        fight.OneInput.Hold(5, 10, InputFlags.DPadDown).Tap(5, InputFlags.LeftTrigger);

        fight.Run(12);

        Assert.Equal(5, fight.StartOf("shove"));
    }

    [Fact]
    public void CrouchingThenLettingGoKicksStanding()
    {
        var fight = new TestFight();
        fight.OneInput.Hold(5, 20, InputFlags.DPadDown).Tap(30, InputFlags.A);

        fight.Run(40);

        Assert.Equal(5, fight.StartOf("crouch"));
        Assert.Equal(30, fight.StartOf("kick_left"));
        Assert.Equal(-1, fight.StartOf("low_kick"));
    }

    [Fact]
    public void KickInTheAirIsTheJumpKick()
    {
        var fight = new TestFight();
        fight.OneInput.Hold(5, 9, InputFlags.DPadUp).Tap(25, InputFlags.A);

        fight.Run(40);

        Assert.Equal(5, fight.StartOf("jump"));
        Assert.Equal(25, fight.StartOf("jump_kick"));
    }

    [Fact]
    public void TheFirstTickIsNotAPress()
    {
        // The button that confirmed the menu is still held on the first tick of the fight, it starts nothing
        var fight = new TestFight();
        fight.OneInput.Hold(1, 3, InputFlags.A);

        fight.Run(12);

        Assert.Equal(-1, fight.StartOf("kick_left"));
    }
}
