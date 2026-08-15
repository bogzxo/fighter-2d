using Fighter2D.Logic;
using Fighter2D.Logic.Moves;

namespace Fighter2D.Character;

/// <summary>
/// Handles Physics groundedness, Stance calculation, and Status Effect locks.
/// </summary>
internal class PlayerStateTracker
{
	public Player Player { get; }
	public bool IsGrounded { get; private set; }

	// Default Stance/Status
	public Stance CurrentStance { get; set; } = Stance.Standing;
	public PlayerStatusType CurrentStatus { get; set; } = PlayerStatusType.Normal;

	public float FallDuration { get; private set; }
	public bool DeltaOnGround { get; private set; }

	private float _statusTimer = 0.0f;

	public PlayerStateTracker(Player player)
	{
		Player = player;
	}

	public void UpdatePhysicsState(float dt)
	{
		DeltaOnGround = IsGrounded;
		CheckGround();
		UpdateStance(dt);
	}

	public void UpdateStatus(float dt)
	{
		// Only decrement timer for timed statuses like Stun or ComboTrap
		// (Attacking, Guarding, etc., are managed manually by the Move Coroutines)
		if (CurrentStatus is PlayerStatusType.Normal or PlayerStatusType.Attacking or PlayerStatusType.Guarding or PlayerStatusType.Invulnerable)
		{
			return;
		}

		_statusTimer -= dt;
        if (!(_statusTimer <= 0)) return;
        
        CurrentStatus = PlayerStatusType.Normal;
        _statusTimer = 0.0f;
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
		var feetFixture = Player.PhysicsBody.KinematicFixtures.Find(f => f.Tag == "feet");
		IsGrounded = feetFixture?.IsTouching ?? false;
	}

	private void UpdateStance(float dt)
	{
		float verticalVelocity = Player.PhysicsBody.Velocity.Y;

		if (IsGrounded)
		{
			CurrentStance = (Player.Controller.CurrentMove.Id == MoveId.Crouch) ? Stance.Crouching : Stance.Standing;
		}
		else
        {
            switch (verticalVelocity)
            {
                case > 0.1f:
                    CurrentStance = Stance.Jumping;
                    FallDuration = 0f;
                    break;
                case <= -0.1f:
                    CurrentStance = Stance.Falling;
                    FallDuration += dt;
                    break;
            }
        }
	}
}