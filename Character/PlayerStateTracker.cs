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

	/// <summary>
	/// How many ticks of the fight the stun we are in has left, 0 when we aren't in one.
	/// </summary>
	public int StunTicks { get; private set; }

	/// <summary>
	/// How many blows have landed on us since we were last free to do anything about it, 0 when we are.
	/// </summary>
	public int ComboHits { get; private set; }

	public bool IsStunned => CurrentStatus == PlayerStatusType.Stunned;

	// Looked up once rather than on every update
	private Horizon.Physics.Fixtures.IPhysicsFixture? _feetFixture;

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

	/// <summary>
	/// Counts a tick off the stun, the other statuses are managed by the moves that set them.
	/// </summary>
	/// <returns>True on the tick the stun runs out.</returns>
	public bool TickStun()
	{
		if (!IsStunned || --StunTicks > 0) return false;

		ClearStun();
		return true;
	}

	/// <summary>
	/// Stuns us for a number of ticks from now, which counts as one more blow of the combo we are in.
	/// </summary>
	public void ApplyStun(int ticks)
	{
		CurrentStatus = PlayerStatusType.Stunned;
		StunTicks = Math.Max(1, ticks);
		ComboHits++;
	}

	/// <summary>
	/// Puts the status where the machine that plays this player says it is.
	/// </summary>
	public void Restore(PlayerStatusType status, int stunTicks, int comboHits)
	{
		CurrentStatus = status;
		StunTicks = status == PlayerStatusType.Stunned ? Math.Max(1, stunTicks) : 0;
		ComboHits = status == PlayerStatusType.Stunned ? comboHits : 0;
	}

	private void ClearStun()
	{
		CurrentStatus = PlayerStatusType.Normal;
		StunTicks = 0;
		ComboHits = 0;
	}

	public void ResetFallDuration() => FallDuration = 0f;

	private void CheckGround()
	{
		_feetFixture ??= Player.PhysicsBody.KinematicFixtures.Find(f => f.Tag == "feet");
		IsGrounded = _feetFixture?.IsTouching ?? false;
	}

	private void UpdateStance(float dt)
	{
		float verticalVelocity = Player.PhysicsBody.Velocity.Y;

		if (IsGrounded)
		{
			// A move that puts us in a stance (crouching) keeps us in it for as long as we are on the ground
			CurrentStance = Player.Controller.CurrentMove.Stance == Stance.Crouching ? Stance.Crouching : Stance.Standing;
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
