using System;

namespace Fighter2D.Simulation;

/// <summary>
/// Where a fighter stands in the fight, apart from the move they are in. On the ground or in the air and in which stance,
/// what they are busy with (attacking, blocking, eating a hit), and the clocks that take the controls away: hitstun, blockstun
/// and lying on the floor after a knockdown. The controller ticks these once per tick of the fight, the HitResolver sets them.
/// </summary>
internal class FighterState(IFighter player)
{
    // How fast (up or down) a player has to be going in the air for it to count as jumping or falling
    private const float AIR_SPEED_THRESHOLD = 0.1f;

    public IFighter Player { get; } = player;
    public bool IsGrounded { get; private set; }

    // Whether we were on the ground on the tick before, for spotting the tick we land on
    public bool WasGrounded { get; private set; }

    public Stance CurrentStance { get; set; } = Stance.Standing;
    public FighterStatus CurrentStatus { get; set; } = FighterStatus.Normal;

    // How long (in seconds) we have been falling for
    public float FallDuration { get; private set; }

    /// <summary>
    /// How many ticks of hitstun are left, 0 when we aren't in it. A knockdown runs on this clock as well.
    /// </summary>
    public int HitstunTicks { get; private set; }

    /// <summary>
    /// How many ticks we are still stuck in our block for after blocking a hit, 0 when we are free to drop it.
    /// </summary>
    public int BlockstunTicks { get; private set; }

    /// <summary>
    /// How many hits have landed on us since we last had the controls, 0 when we do.
    /// </summary>
    public int ComboCount { get; private set; }

    /// <summary>
    /// How much damage those hits have done all together.
    /// </summary>
    public int ComboDamage { get; private set; }

    public bool IsInHitstun => CurrentStatus == FighterStatus.Hitstun;

    /// <summary>
    /// How much of their gravity we fall with right now, 1 unless we are in hitstun, where the hit that put us there said
    /// (or our character did) and every hit in the air after the first adds a bit, so a juggle comes down by itself.
    /// </summary>
    public float GravityShare { get; private set; } = 1.0f;

    /// <summary>
    /// How many hits have landed on us while we were already up in the air in hitstun, the juggle so far.
    /// </summary>
    public int JuggleHits { get; private set; }

    /// <summary>
    /// How many jumps we have left before we touch the ground again, see CharacterMovement.DoubleJumps.
    /// </summary>
    public int AirJumpsLeft { get; set; }

    /// <summary>
    /// Whether we are fast falling, down pressed past the top of a jump. Over when we land or go up again.
    /// </summary>
    public bool FastFalling { get; set; }

    /// <summary>
    /// How many more ticks we are stuck in the landing lag of an aerial we came down in the middle of.
    /// </summary>
    public int LandingLagTicks { get; private set; }

    public bool IsInBlockstun => BlockstunTicks > 0;

    /// <summary>
    /// Whether we are lying on the floor after a knockdown. Nothing hits somebody who is down, they get up when the hitstun runs out.
    /// </summary>
    public bool IsDowned { get; private set; }

    /// <summary>
    /// Whether the hit we are eating puts us on the floor, once we come down from it.
    /// </summary>
    public bool KnockdownPending { get; private set; }

    /// <summary>
    /// Whether this is the tick we touched down on.
    /// </summary>
    public bool JustLanded => IsGrounded && !WasGrounded;

    public void UpdatePhysicsState(float dt)
    {
        WasGrounded = IsGrounded;
        CheckGround();
        UpdateStance(dt);

        if (IsGrounded)
        {
            AirJumpsLeft = Player.Character.Movement.DoubleJumps;
            FastFalling = false;
        }
        else if (Player.Body.Velocity.Y > AIR_SPEED_THRESHOLD) FastFalling = false;
    }

    /// <summary>
    /// Got up out of a knockdown before it happened, a tech. The hitstun is over and nothing is pending.
    /// </summary>
    public void Tech() => ClearHitstun();

    /// <summary>
    /// Counts one tick off the landing lag.
    /// </summary>
    public void TickLandingLag()
    {
        if (LandingLagTicks > 0) LandingLagTicks--;
    }

    /// <summary>
    /// Sticks us in the landing lag of an aerial for a number of ticks.
    /// </summary>
    public void StartLandingLag(int ticks) => LandingLagTicks = Math.Max(0, ticks);

    /// <summary>
    /// Counts one tick off the hitstun. The other statuses are managed by the moves that set them.
    /// </summary>
    /// <returns>True on the tick the hitstun runs out.</returns>
    public bool TickHitstun()
    {
        if (!IsInHitstun || --HitstunTicks > 0) return false;

        ClearHitstun();
        return true;
    }

    /// <summary>
    /// Counts one tick off the blockstun.
    /// </summary>
    /// <returns>True on the tick the blockstun runs out.</returns>
    public bool TickBlockstun() => BlockstunTicks > 0 && --BlockstunTicks == 0;

    /// <summary>
    /// Puts us in hitstun for a number of ticks from now, which counts as one more hit of the combo we are eating.
    /// </summary>
    /// <param name="damage">How much the hit that did it took off us, for the combo counter.</param>
    /// <param name="knockdown">Whether the hit puts us on the floor once we come down.</param>
    /// <param name="gravityShare">How much of our gravity we fall with until it is over.</param>
    /// <param name="juggle">Whether we were already up in the air in hitstun when it landed.</param>
    public void ApplyHitstun(int ticks, int damage, bool knockdown = false, float gravityShare = 1.0f, bool juggle = false)
    {
        CurrentStatus = FighterStatus.Hitstun;
        HitstunTicks = Math.Max(1, ticks);
        GravityShare = gravityShare;
        if (juggle) JuggleHits++;
        LandingLagTicks = 0;
        BlockstunTicks = 0;
        ComboCount++;
        ComboDamage += damage;
        KnockdownPending |= knockdown;
    }

    /// <summary>
    /// A low that caught us lying on the floor. It counts towards the combo we are eating, and that's all, we stay down
    /// for as long as we were going to.
    /// </summary>
    public void TakeDownHit(int damage)
    {
        ComboCount++;
        ComboDamage += damage;
    }

    /// <summary>
    /// Keeps us in our block for a number of ticks, the longer of this and what is left.
    /// </summary>
    public void ApplyBlockstun(int ticks) => BlockstunTicks = Math.Max(BlockstunTicks, ticks);

    /// <summary>
    /// Puts us on the floor for a number of ticks. The combo is over, nobody can hit us down here.
    /// </summary>
    public void KnockDown(int ticks)
    {
        CurrentStatus = FighterStatus.Hitstun;
        HitstunTicks = Math.Max(1, ticks);
        BlockstunTicks = 0;
        GravityShare = 1.0f;
        IsDowned = true;
        KnockdownPending = false;
    }

    /// <summary>
    /// Sets the status to whatever the machine that plays this player says it is.
    /// </summary>
    public void Restore(FighterStatus status, int hitstunTicks, int comboCount, int blockstunTicks)
    {
        bool hitstun = status == FighterStatus.Hitstun;

        CurrentStatus = status;
        HitstunTicks = hitstun ? Math.Max(1, hitstunTicks) : 0;
        BlockstunTicks = Math.Max(0, blockstunTicks);
        ComboCount = hitstun ? comboCount : 0;
        if (!hitstun)
        {
            GravityShare = 1.0f;
            JuggleHits = 0;
            ComboDamage = 0;
            IsDowned = false;
            KnockdownPending = false;
        }
    }

    private void ClearHitstun()
    {
        CurrentStatus = FighterStatus.Normal;
        GravityShare = 1.0f;
        JuggleHits = 0;
        HitstunTicks = 0;
        ComboCount = 0;
        ComboDamage = 0;
        IsDowned = false;
        KnockdownPending = false;
    }

    public void ResetFallDuration() => FallDuration = 0f;

    private void CheckGround()
    {
        IsGrounded = Player.Body.IsGrounded;
    }

    private void UpdateStance(float dt)
    {
        if (IsGrounded)
        {
            // A block that is held along with down is a crouching block, which is what stops a low. The stance is locked
            // for as long as the blockstun lasts, nobody gets to stand up halfway through eating a string
            if (IsInBlockstun) return;

            var controller = Player.Controller;
            bool crouching = controller.CurrentMove.Stance == Stance.Crouching
                || (CurrentStatus == FighterStatus.Blocking && controller.Inputs.IsHeld(InputFlags.DPadDown));

            CurrentStance = crouching ? Stance.Crouching : Stance.Standing;
            return;
        }

        float verticalVelocity = Player.Body.Velocity.Y;

        if (verticalVelocity > AIR_SPEED_THRESHOLD)
        {
            CurrentStance = Stance.Jumping;
            FallDuration = 0f;
        }
        else if (verticalVelocity <= -AIR_SPEED_THRESHOLD)
        {
            CurrentStance = Stance.Falling;
            FallDuration += dt;
        }
    }
}
