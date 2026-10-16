using Fighter2D.Data;
using Fighter2D.Tests.Support;

namespace Fighter2D.Tests.Simulation;

/// <summary>
/// What a hit does to whoever it lands on. Hitstun is the move's number to the frame, a block costs chip and blockstun,
/// a block that only just went up is a parry, a hit landed again and again goes stale, and block just before a knockdown
/// lands is a tech. The ones step 3 of the refactor plan promised and the fight's first tests didn't get to.
/// </summary>
public class HitTests
{
    // Blocky's roundhouse, B, comes out on frame 15, 11 damage and 44 frames of hitstun
    private const int ROUNDHOUSE_DAMAGE = 11;
    private const int ROUNDHOUSE_HITSTUN = 44;

    [Fact]
    public void HitstunIsWhatTheMoveSaysToTheFrame()
    {
        var fight = new TestFight(apart: 40.0f);
        fight.OneInput.Tap(5, InputFlags.B);

        // The hitlag freezes both of them before the stun starts running, it doesn't count
        int stunned = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            fight.Run(1);
            if (fight.Two.Controller.State.IsInHitstun && !fight.Two.Controller.IsFrozen) stunned++;
        }

        Assert.Equal(CombatRules.MAX_HEALTH - ROUNDHOUSE_DAMAGE, fight.Two.Health);
        Assert.Equal(ROUNDHOUSE_HITSTUN, stunned);
    }

    [Fact]
    public void ABlockTakesChipAndBlockstunAndNoHitstun()
    {
        var fight = new TestFight(apart: 40.0f);
        fight.OneInput.Tap(5, InputFlags.B);
        fight.TwoInput.Hold(1, 80, InputFlags.RightBumper);

        bool blockstun = false, hitstun = false, attackerStunned = false;
        for (int tick = 0; tick < 80; tick++)
        {
            fight.Run(1);
            blockstun |= fight.Two.Controller.State.IsInBlockstun;
            hitstun |= fight.Two.Controller.State.IsInHitstun;
            attackerStunned |= fight.One.Controller.State.IsInHitstun;
        }

        Assert.True(blockstun, "a blocked roundhouse should leave the blocker in blockstun");
        Assert.False(hitstun, "a blocked roundhouse is no hit");
        Assert.False(attackerStunned, "a block held long before the hit is no parry");
        Assert.InRange(fight.Two.Health, CombatRules.MAX_HEALTH - ROUNDHOUSE_DAMAGE + 1, CombatRules.MAX_HEALTH);
    }

    [Fact]
    public void ABlockThatOnlyJustWentUpIsAParry()
    {
        // The roundhouse lands on tick 20 and a block takes five ticks to go up, so held from 12 it is up three before
        var fight = new TestFight(apart: 40.0f);
        fight.OneInput.Tap(5, InputFlags.B);
        fight.TwoInput.Hold(12, 80, InputFlags.RightBumper);

        int attackerStunned = 0;
        bool blockstun = false;
        for (int tick = 0; tick < 80; tick++)
        {
            fight.Run(1);
            if (fight.One.Controller.State.IsInHitstun && !fight.One.Controller.IsFrozen) attackerStunned++;
            blockstun |= fight.Two.Controller.State.IsInBlockstun;
        }

        Assert.Equal((byte)CombatRules.MAX_HEALTH, fight.Two.Health);
        Assert.False(blockstun, "a parry has no blockstun");
        Assert.Equal(CombatRules.PARRY_STUN, attackerStunned);
    }

    [Fact]
    public void AMoveThatKeepsLandingGoesStale()
    {
        var fight = new TestFight();
        PlayerController controller = fight.One.Controller;
        Assert.True(controller.MoveList.TryGetMove("kick_right", out FightingMove roundhouse));
        Assert.True(controller.MoveList.TryGetMove("punch_left", out FightingMove jab));

        Assert.Equal(1.0f, controller.StaleScale(roundhouse));

        controller.RememberLanded(roundhouse);
        controller.RememberLanded(jab);
        controller.RememberLanded(roundhouse);

        // The newest landing costs the most, the one before it less, and a move that wasn't landed is fresh
        float stale = 1.0f - CombatRules.STALE_STEPS[0] - CombatRules.STALE_STEPS[2];
        Assert.Equal(stale, controller.StaleScale(roundhouse), 4);
        Assert.Equal(1.0f - CombatRules.STALE_STEPS[1], controller.StaleScale(jab), 4);
        Assert.True(controller.MoveList.TryGetMove("hook", out FightingMove hook));
        Assert.Equal(1.0f, controller.StaleScale(hook));
    }

    [Fact]
    public void BlockJustBeforeAKnockdownLandsTechsIt()
    {
        // When the sweep's victim comes down with nobody pressing anything, worked out by playing it once
        int landed = KnockdownLanding(blockAt: -1, out string untouched);
        Assert.True(landed > 0, "the sweep should have knocked them down");
        Assert.Equal(MoveIds.KNOCKED_DOWN, untouched);

        // Block a few frames before that is a tech, the same press long before is locked out and they lie there
        KnockdownLanding(blockAt: landed - 3, out string teched);
        Assert.Contains(teched, new[] { MoveIds.TECH, MoveIds.GET_UP });

        KnockdownLanding(blockAt: landed - CombatRules.TECH_WINDOW - 6, out string early);
        Assert.Equal(MoveIds.KNOCKED_DOWN, early);
    }

    // Blocky sweeps (down and B) the other Blocky, who taps block on a tick (or not at all), and says the tick the victim
    // came down on after being up in the air in hitstun and what they went into once down
    private static int KnockdownLanding(int blockAt, out string into)
    {
        var fight = new TestFight(apart: 40.0f);
        fight.OneInput.Hold(5, 6, InputFlags.DPadDown | InputFlags.B);
        if (blockAt > 0) fight.TwoInput.Tap(blockAt, InputFlags.RightBumper);

        bool wasUp = false;
        for (int tick = 0; tick < 200; tick++)
        {
            fight.Run(1);
            PlayerController two = fight.Two.Controller;
            bool up = !fight.Two.IsGrounded;
            if (wasUp && !up)
            {
                // What they lie in (or get up with) is started the tick after their feet touch
                int landed = fight.TwoInput.Tick;
                fight.Run(1);
                into = two.CurrentMove.Id;
                return landed;
            }
            wasUp |= up && fight.Two.Controller.State.IsInHitstun;
        }

        into = fight.Two.Controller.CurrentMove.Id;
        return -1;
    }
}
