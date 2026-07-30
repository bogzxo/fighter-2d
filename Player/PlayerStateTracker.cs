using CumInstinctDuel.Logic;

namespace CumInstinctDuel.Player;

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
        IsGrounded = false;
        var body = Player.Instance.PlayerBody;
        if (body == null) return;

        // Velocity gate: Rapid upward movement overrides physical touch
        if (body.GetLinearVelocity().Y > 0.15f) return;

        for (var edge = body.GetContactList(); edge != null; edge = edge.next)
        {
            var contact = edge.contact;
            if (contact == null || !contact.IsTouching()) continue;

            bool isFootContact = (contact.FixtureA.UserData as string == "FootSensor") ||
                                 (contact.FixtureB.UserData as string == "FootSensor");

            if (isFootContact)
            {
                IsGrounded = true;
                return;
            }
        }
    }

    private void UpdateStance(float dt, bool isCrouchingMove)
    {
        float verticalVelocity = Player.Instance.PlayerBody.GetLinearVelocity().Y;

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