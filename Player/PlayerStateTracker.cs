using Fighter2D.Logic;

namespace Fighter2D.Player;

/// <summary>
/// Handles Physics groundedness, Stance calculation, and Status Effect locks.
/// </summary>
internal class PlayerStateTracker
{
    public bool IsGrounded { get; private set; }
    public Stance CurrentStance { get; private set; } = Stance.Standing;
    public PlayerStatusType CurrentStatus { get; private set; } = PlayerStatusType.Normal;

    public float FallDuration { get; private set; }
    public bool DeltaOnGround { get; private set; }

    private float _statusTimer = 0.0f;

    public void UpdatePhysicsState(float dt, bool isCrouchingMove)
    {
        DeltaOnGround = IsGrounded;
        CheckGround();
        UpdateStance(dt, isCrouchingMove);
    }

    public void UpdateStatus(float dt)
    {
        if (CurrentStatus == PlayerStatusType.Normal) return;

        _statusTimer -= dt;
        if (_statusTimer <= 0)
        {
            CurrentStatus = PlayerStatusType.Normal;
            _statusTimer = 0.0f;
        }
    }

    public void ApplyStun(float duration)
    {
        CurrentStatus = PlayerStatusType.Stunned;
        _statusTimer = duration;
    }

    public void ApplyComboTrap(float duration)
    {
        CurrentStatus = PlayerStatusType.ComboTrapped;
        _statusTimer = duration;
    }

    public void ResetFallDuration() => FallDuration = 0f;

    private void CheckGround()
    {
        var feetFixture = Player.Instance.PhysicsBody.KinematicFixtures.Find(f => f.Tag == "feet");
        IsGrounded = feetFixture?.IsTouching ?? false;
    }

    private void UpdateStance(float dt, bool isCrouchingMove)
    {
        float verticalVelocity = Player.Instance.PhysicsBody.Velocity.Y;

        if (IsGrounded)
        {
            CurrentStance = isCrouchingMove ? Stance.Crouching : Stance.Standing;
        }
        else
        {
            if (verticalVelocity > 0.1f)
            {
                CurrentStance = Stance.Jumping;
                FallDuration = 0f;
            }
            else if (verticalVelocity <= -0.1f)
            {
                CurrentStance = Stance.Falling;
                FallDuration += dt;
            }
        }
    }
}