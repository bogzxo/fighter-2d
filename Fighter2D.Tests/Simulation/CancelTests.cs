using Fighter2D.Tests.Support;

namespace Fighter2D.Tests.Simulation;

/// <summary>
/// When a move lets go. A hit that lands buys room, a whiff doesn't, and a launcher jump cancels.
/// </summary>
public class CancelTests
{
    // Blocky's roundhouse hits on frame 15 for 5 frames, 11 damage is 13 frames of hitlag, and a hit that landed lets
    // movement in HIT_MOVEMENT_CANCEL frames after the last active frame. Measured the same in a scripted fight
    // The rules are read off the content, so this is worked out once a fight has read them
    private static int ROUNDHOUSE_RUN => 15 + 13 + 4 + CombatRules.HIT_MOVEMENT_CANCEL;

    [Fact]
    public void ARoundhouseThatLandsLetsYouRun()
    {
        var fight = new TestFight(apart: 40.0f);
        fight.OneInput.Tap(5, InputFlags.B).Hold(10, 80, InputFlags.DPadRight);

        fight.Run(80);

        Assert.Equal(5, fight.StartOf("kick_right"));
        Assert.True(fight.Two.Health < CombatRules.MAX_HEALTH, "the roundhouse should have landed");
        Assert.Equal(5 + ROUNDHOUSE_RUN, fight.StartOf("run"));
    }

    [Fact]
    public void ARoundhouseThatWhiffsPlaysOut()
    {
        var fight = new TestFight(apart: 300.0f);
        fight.OneInput.Tap(5, InputFlags.B).Hold(10, 120, InputFlags.DPadRight);

        fight.Run(120);

        int ran = fight.StartOf("run");
        Assert.True(ran > 5 + ROUNDHOUSE_RUN, $"ran at {ran}, a whiff should have held him longer than a hit");
        Assert.Equal((byte)CombatRules.MAX_HEALTH, fight.Two.Health);
    }

    [Fact]
    public void TheUppercutJumpCancelsTheMomentTheHitlagIsOver()
    {
        // Hit on frame 20, 9 damage is 11 frames of hitlag, and its own cancel frame (25) would only come 5 frames later
        var fight = new TestFight(apart: 40.0f);
        fight.OneInput.Tap(5, InputFlags.RightTrigger).Hold(20, 60, InputFlags.DPadUp);

        fight.Run(60);

        Assert.Equal(5, fight.StartOf("uppercut"));
        Assert.Equal(5 + 20 + 11, fight.StartOf("jump"));
        Assert.True(fight.Two.Controller.State.IsInHitstun || fight.Two.Body.Position.Y > 70.0f, "the victim should be up in the air");
    }

    [Fact]
    public void TwoButtonsAFewTicksApartAreTheTwoButtonMove()
    {
        var fight = new TestFight();
        fight.One.Meter = 50;
        fight.OneInput.Hold(5, 10, InputFlags.A).Hold(7, 10, InputFlags.B);

        fight.Run(20);

        Assert.Equal(5, fight.StartOf("kick_left"));
        Assert.Equal(7, fight.StartOf("spin_kick"));
    }

    [Fact]
    public void WithoutTheMeterTheTwoButtonMoveIsAPlainKick()
    {
        var fight = new TestFight();
        fight.One.Meter = 0;
        fight.OneInput.Hold(5, 10, InputFlags.A).Hold(7, 10, InputFlags.B);

        fight.Run(20);

        Assert.Equal(5, fight.StartOf("kick_left"));
        Assert.Equal(-1, fight.StartOf("spin_kick"));
    }
}
