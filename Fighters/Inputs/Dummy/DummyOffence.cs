using System;

using Fighter2D.Logic;

namespace Fighter2D.Character.Controllers.Dummy;

/// <summary>
/// The offensive half of the dummy's brain. Throws attacks when the other player is in range, punishes whiffs and follows up on hitstun.
/// </summary>
internal sealed class DummyOffence(PlayerController controller, DummyHands hands)
{
    private float _attackTimer = DummyConfig.MAX_ATTACK_DELAY;

    // What it saw on the last tick, so every opening is only decided on once
    private bool _sawRecovery, _sawHitstun, _airAttacked;
    private int _followUps;

    /// <summary>
    /// Called every tick the dummy is free to attack.
    /// </summary>
    public void Update(in DummyView view)
    {
        _attackTimer -= DummyConfig.TICK;

        if (view.Grounded) _airAttacked = false;

        if (FollowUp(view)) return;

        Punish(view);
        AttackInTheAir(view);
        AttackInNeutral(view);
    }

    /// <summary>
    /// Helper method to keep hitting somebody who is in hitstun, a few times per hitstun.
    /// </summary>
    /// <returns>True if it pressed something.</returns>
    private bool FollowUp(in DummyView view)
    {
        bool hitstun = view.Them.StateTracker.IsInHitstun;

        // Decided once when the hitstun starts, either it goes for the combo or it doesn't
        if (!hitstun) _followUps = 0;
        if (hitstun && !_sawHitstun) _followUps = Random.Shared.NextSingle() < DummyConfig.FOLLOW_UP_CHANCE ? DummyConfig.MAX_FOLLOW_UPS : 0;
        _sawHitstun = hitstun;

        if (!hitstun || !view.CanReach || _followUps <= 0 || !controller.CanStartMove || !hands.CanPress) return false;

        // Punches are the quickest thing it has, and there is no telling how much hitstun is left
        hands.Press(DummyHands.Punch());
        _followUps--;
        return true;
    }

    /// <summary>
    /// Helper method to hit somebody who is stuck in the recovery of their own attack.
    /// </summary>
    private void Punish(in DummyView view)
    {
        bool recovering = view.Them.IsRecovering;

        if (recovering && !_sawRecovery && view.CanReach && hands.CanPress && Random.Shared.NextSingle() < DummyConfig.PUNISH_CHANCE)
        {
            hands.Press(DummyHands.Punch());
            _attackTimer = MathF.Max(_attackTimer, DummyConfig.MIN_ATTACK_DELAY);
        }

        _sawRecovery = recovering;
    }

    /// <summary>
    /// Helper method to throw a kick on the way past somebody we are in the air next to, once per jump.
    /// </summary>
    private void AttackInTheAir(in DummyView view)
    {
        if (view.Grounded || _airAttacked || view.FacingAway) return;
        if (view.ToOpponent.Length() > DummyConfig.ATTACK_RANGE * DummyConfig.AIR_ATTACK_REACH) return;

        _airAttacked = true;
        if (Random.Shared.NextSingle() < DummyConfig.AIR_ATTACK_CHANCE) hands.Press(DummyHands.Kick());
    }

    /// <summary>
    /// Helper method for the attacks it throws without anybody giving it an opening.
    /// </summary>
    private void AttackInNeutral(in DummyView view)
    {
        if (!view.InRange)
        {
            // The reaction time starts over whenever they are out of range, so the player always gets the first go
            _attackTimer = MathF.Max(_attackTimer, DummyConfig.REACTION_TIME);
            return;
        }

        // No point swinging at somebody with i-frames or with our back to them
        if (_attackTimer > 0 || view.FacingAway || view.Them.StateTracker.CurrentStatus == PlayerStatusType.Invulnerable) return;

        // Press one of the attacks, then leave a gap for the player to hit back
        hands.Press(Random.Shared.NextSingle() < DummyConfig.PUNCH_CHANCE ? DummyHands.Punch() : DummyHands.Kick());
        _attackTimer = DummyConfig.MIN_ATTACK_DELAY + Random.Shared.NextSingle() * (DummyConfig.MAX_ATTACK_DELAY - DummyConfig.MIN_ATTACK_DELAY);
    }
}
