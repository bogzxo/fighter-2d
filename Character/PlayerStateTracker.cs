using System;

using Fighter2D.Logic;

using Horizon.Physics.Fixtures;

namespace Fighter2D.Character;

/// <summary>
/// Handles Physics groundedness, Stance calculation, and the hitstun timer.
/// </summary>
internal class PlayerStateTracker(Player player)
{
    // How fast (up or down) a player has to be going in the air for it to count as jumping or falling
    private const float AIR_SPEED_THRESHOLD = 0.1f;

    public Player Player { get; } = player;
    public bool IsGrounded { get; private set; }

    // Whether we were on the ground on the tick before, for spotting the tick we land on
    public bool WasGrounded { get; private set; }

    public Stance CurrentStance { get; set; } = Stance.Standing;
    public PlayerStatusType CurrentStatus { get; set; } = PlayerStatusType.Normal;

    // How long (in seconds) we have been falling for
    public float FallDuration { get; private set; }

    /// <summary>
    /// How many ticks of hitstun are left, 0 when we aren't in it.
    /// </summary>
    public int HitstunTicks { get; private set; }

    /// <summary>
    /// How many hits have landed on us since we last had the controls, 0 when we do.
    /// </summary>
    public int ComboCount { get; private set; }

    public bool IsInHitstun => CurrentStatus == PlayerStatusType.Hitstun;

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
    /// Puts us in hitstun for a number of ticks from now, which counts as one more hit of the combo we are eating.
    /// </summary>
    public void ApplyHitstun(int ticks)
    {
        CurrentStatus = PlayerStatusType.Hitstun;
        HitstunTicks = Math.Max(1, ticks);
        ComboCount++;
    }

    /// <summary>
    /// Sets the status to whatever the machine that plays this player says it is.
    /// </summary>
    public void Restore(PlayerStatusType status, int hitstunTicks, int comboCount)
    {
        bool hitstun = status == PlayerStatusType.Hitstun;

        CurrentStatus = status;
        HitstunTicks = hitstun ? Math.Max(1, hitstunTicks) : 0;
        ComboCount = hitstun ? comboCount : 0;
    }

    private void ClearHitstun()
    {
        CurrentStatus = PlayerStatusType.Normal;
        HitstunTicks = 0;
        ComboCount = 0;
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
            // A move that puts us in a crouch keeps us in it for as long as we are on the ground
            CurrentStance = Player.Controller.CurrentMove.Stance == Stance.Crouching ? Stance.Crouching : Stance.Standing;
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
