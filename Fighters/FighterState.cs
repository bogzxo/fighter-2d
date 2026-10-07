using System;

using Horizon.Physics.Fixtures;

namespace Fighter2D.Fighters;

/// <summary>
/// Where a fighter stands in the fight, apart from the move they are in. On the ground or in the air and in which stance,
/// what they are busy with (attacking, blocking, eating a hit), and the clocks that take the controls away: hitstun, blockstun
/// and lying on the floor after a knockdown. The controller ticks these once per tick of the fight, the HitResolver sets them.
/// </summary>
internal class FighterState(Player player)
{
    // How fast (up or down) a player has to be going in the air for it to count as jumping or falling
    private const float AIR_SPEED_THRESHOLD = 0.1f;

    public Player Player { get; } = player;
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

    // Looked up once rather than on every update
    private IPhysicsFixture? _feetFixture;

    public void UpdatePhysicsState(float dt)
    {
        WasGrounded = IsGrounded;
        CheckGround();
        UpdateStance(dt);
    }

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
    public void ApplyHitstun(int ticks, int damage, bool knockdown = false)
    {
        CurrentStatus = FighterStatus.Hitstun;
        HitstunTicks = Math.Max(1, ticks);
        BlockstunTicks = 0;
        ComboCount++;
        ComboDamage += damage;
        KnockdownPending |= knockdown;
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
            ComboDamage = 0;
            IsDowned = false;
            KnockdownPending = false;
        }
    }

    private void ClearHitstun()
    {
        CurrentStatus = FighterStatus.Normal;
        HitstunTicks = 0;
        ComboCount = 0;
        ComboDamage = 0;
        IsDowned = false;
        KnockdownPending = false;
    }

    public void ResetFallDuration() => FallDuration = 0f;

    private void CheckGround()
    {
        _feetFixture ??= Player.PhysicsBody.KinematicFixtures.Find(f => f.Tag == Player.FEET_TAG);
        IsGrounded = _feetFixture?.IsTouching ?? false;
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

        float verticalVelocity = Player.PhysicsBody.Velocity.Y;

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
