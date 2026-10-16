using System;
using System.Numerics;

namespace Fighter2D.Simulation;

/// <summary>
/// The one place that decides whether a hit lands and what it does.
/// Every hit of the game comes through here no matter who threw it (a local player, the dummy, a network player) and who took it.
/// The numbers it uses are in <see cref="CombatRules"/>.
/// </summary>
internal static class HitResolver
{
    /// <summary>
    /// Called by a move on every one of its active frames (from its hit frame on, for as many as it has, see
    /// <see cref="FightingMove.ActiveFrames"/>) until it lands. Tests the attacker's hitbox against the opponent's
    /// hurtbox and resolves the hit if they touch.
    /// </summary>
    /// <param name="frame">The frame of the animation that is showing, which is what the hitbox is read from.</param>
    /// <param name="lastChance">Whether this is the last active frame, a miss on it is a whiff. One before it is nothing yet.</param>
    /// <returns>Whether it touched, the move's hit is spent.</returns>
    public static bool TryLand(PlayerController attacker, uint frame, bool lastChance = true)
    {
        // A network player doesn't get to decide anything here, their hits and whiffs arrive as messages (see FightNetwork)
        if (attacker.IsRemote) return true;

        IFighter player = attacker.Player, opponent = attacker.Opponent;
        FightingMove move = attacker.CurrentMove;

        if (!TryConnect(player, opponent, frame, out Box hitbox))
        {
            if (!lastChance) return false;

            // Swung at nothing. The other machine can't see that for itself, so it gets told
            player.GainMeter(CombatRules.METER_ON_WHIFF);
            attacker.Fight.CombatLog.Report(new AttackReport(player, move, HitResult.Whiff));
            if (opponent.Controller.IsRemote) attacker.Fight.Network?.SendWhiff(move.Id);
            return false;
        }

        // Where the two boxes meet is where it landed
        hitbox.Overlaps(opponent.Body.Hurtbox, out Vector2 impact);
        float direction = opponent.Body.Position.X < player.Body.Position.X ? -1 : 1;

        // Online the machine of whoever got hit has the last word. Until it answers we go by what it looked like from here
        bool predicted = opponent.Controller.IsRemote;
        if (predicted) attacker.Fight.Network?.SendHit(move.Id, direction, impact);

        Resolve(attacker, opponent.Controller, move, direction, impact, predicted);
        return true;
    }

    /// <summary>
    /// Decides what a hit comes to and applies it.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    /// <param name="predicted">Whether this is only our guess, with the real result (and the health of the victim) decided on another machine.</param>
    public static HitResult Resolve(PlayerController attacker, PlayerController victim, FightingMove move, float direction, Vector2 impact, bool predicted)
    {
        HitResult result = Judge(victim, move, direction);
        bool lethal = false;

        switch (result)
        {
            case HitResult.Blocked:
                Block(attacker, victim, move, direction, predicted);
                break;

            case HitResult.Parried:
                Parry(attacker, victim);
                break;

            case HitResult.Whiff:
                break;

            default:
                lethal = victim.State.IsDowned
                    ? LandOnDowned(attacker, victim, move, predicted)
                    : Land(attacker, victim, move, result, direction, predicted);
                break;
        }

        HitFeedback.Play(attacker, victim, move, result, direction, impact, lethal);

        var state = victim.State;
        bool landed = result is not (HitResult.Whiff or HitResult.Blocked or HitResult.Parried);
        attacker.Fight.CombatLog.Report(new AttackReport(attacker.Player, move, result, landed ? state.ComboCount : 0, landed ? state.ComboDamage : 0));

        return result;
    }

    /// <summary>
    /// Helper method for a hit that got through. Damage (scaled down the longer the combo), the shove, the hitstun and the meter both sides earn off it.
    /// </summary>
    /// <returns>Whether this was the hit that took the last of the victim's health.</returns>
    private static bool Land(PlayerController attacker, PlayerController victim, FightingMove move, HitResult result, float direction, bool predicted)
    {
        bool counter = result == HitResult.CounterHit;
        int damage = DamageOf(attacker, move, counter, victim.State.ComboCount);
        attacker.RememberLanded(move);

        // Going by the health we know of. For a network player that is whatever their last snapshot said
        bool lethal = damage >= victim.Player.Health;
        int healthAfter = Math.Max(0, victim.Player.Health - damage);

        if (!predicted) victim.Player.Health = (byte)healthAfter;

        attacker.Player.GainMeter((int)MathF.Round(damage * CombatRules.METER_PER_DAMAGE_DEALT));
        victim.Player.GainMeter((int)MathF.Round(damage * CombatRules.METER_PER_DAMAGE_TAKEN));

        // Already up in the air in hitstun, this is a juggle and they come down a bit heavier every time
        var state = victim.State;
        bool juggle = state.IsInHitstun && !state.IsGrounded;
        float gravityShare = GravityShareOf(victim, move, juggle);

        Send(attacker, victim, move, direction, gravityShare, CombatRules.MAX_HEALTH - healthAfter);

        // Exactly what the move says, a counter hit a few frames more
        int hitstun = move.FrameData.Hitstun + (counter ? CombatRules.COUNTER_HITSTUN_BONUS : 0);
        victim.ApplyHitstun(hitstun, damage, move.Knockdown, gravityShare, juggle);

        // A knockdown that doesn't lift them off the ground puts them down right here, the others wait for the landing
        if (move.Knockdown && !move.Launches && victim.State.IsGrounded) victim.KnockDown();

        return lethal;
    }

    /// <summary>
    /// Helper method for a low that caught somebody lying on the floor. It hurts (scaled like the rest of the combo it
    /// carries on) and earns meter, but they stay where they are, no shove, no new hitstun and their time on the floor
    /// isn't made any longer, so it can't be done forever. A knockdown is still a knockdown.
    /// </summary>
    private static bool LandOnDowned(PlayerController attacker, PlayerController victim, FightingMove move, bool predicted)
    {
        int damage = DamageOf(attacker, move, false, victim.State.ComboCount);
        attacker.RememberLanded(move);
        bool lethal = damage >= victim.Player.Health;

        if (!predicted) victim.Player.Health = (byte)Math.Max(0, victim.Player.Health - damage);

        attacker.Player.GainMeter((int)MathF.Round(damage * CombatRules.METER_PER_DAMAGE_DEALT));
        victim.Player.GainMeter((int)MathF.Round(damage * CombatRules.METER_PER_DAMAGE_TAKEN));
        victim.State.TakeDownHit(damage);
        return lethal;
    }

    /// <summary>
    /// Helper method for a hit that was blocked. A sliver of chip damage, the blockstun that keeps the block up, a shove back and a bit of meter.
    /// </summary>
    private static void Block(PlayerController attacker, PlayerController victim, FightingMove move, float direction, bool predicted)
    {
        // Chip never finishes anybody off, a round ends on a hit
        int chip = (int)MathF.Round(move.Damage * CombatRules.CHIP_DAMAGE_SCALE);
        if (!predicted && chip > 0) victim.Player.Health = (byte)Math.Max(1, victim.Player.Health - chip);

        attacker.Player.GainMeter(CombatRules.METER_ON_BLOCKED);
        victim.Player.GainMeter(CombatRules.METER_ON_BLOCK);

        IFighterBody body = victim.Body;
        victim.QueueLaunch(new Vector2(direction * victim.Player.Character.Movement.SpeedToSlide(CombatRules.BLOCK_PUSH), body.Velocity.Y), influenced: false);
        victim.ApplyBlockstun(move.FrameData.Blockstun);
    }

    /// <summary>
    /// Helper method for a hit that was parried, the block went up just as it came in. Nothing happens to whoever parried
    /// it, no chip, no blockstun and no shove, and the attacker is stuck for a while. A bit more meter than a block.
    /// </summary>
    private static void Parry(PlayerController attacker, PlayerController victim)
    {
        victim.Player.GainMeter(CombatRules.METER_ON_BLOCK * 2);
        attacker.ApplyHitstun(CombatRules.PARRY_STUN);
    }

    /// <summary>
    /// Helper method to test if the attacker's hitbox touches the opponent's hurtbox on this frame.
    /// </summary>
    private static bool TryConnect(IFighter attacker, IFighter opponent, uint frame, out Box hitbox)
    {
        hitbox = default;

        // You can't hit somebody with your back turned to them
        if (!attacker.Body.IsFacing(opponent.Body)) return false;

        hitbox = attacker.Body.Hitbox(frame);
        return attacker.Body.HitboxTouches(frame, opponent.Body);
    }

    /// <summary>
    /// Helper method to decide whether a hit that reached somebody counts, going by the state they are in and where the hit comes in.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    private static HitResult Judge(PlayerController victim, FightingMove move, float direction)
    {
        var state = victim.State;

        // Dodge rolls go straight through hits
        if (state.CurrentStatus == FighterStatus.Invulnerable) return HitResult.Whiff;

        // Somebody lying on the floor is only reached down there, by a low. Nothing standing up goes over their head and
        // hits them, a sweep or a crouching kick does, and it can't be blocked from down there either
        if (state.IsDowned) return move.Level == HitLevel.Low ? HitResult.Hit : HitResult.Whiff;

        // Blocking only covers the front (a hit from behind travels the same way the victim is facing), and only the
        // level the block is held at: crouched for a low, standing for an overhead, either for a mid
        if (victim.IsBlocking && direction * victim.Body.Facing < 0 && Covers(state.CurrentStance, move.Level))
        {
            // A block that only just went up is a parry
            return victim.Tick - victim.BlockStartedAt <= CombatRules.PARRY_TICKS ? HitResult.Parried : HitResult.Blocked;
        }

        // Caught winding up their own attack, should have blocked
        if (victim.IsInStartup) return HitResult.CounterHit;

        // Caught after their own attack already came out, that is what you get for swinging at nothing
        if (victim.IsRecovering) return HitResult.Punish;

        return HitResult.Hit;
    }

    /// <summary>
    /// Helper method to say whether a block held in a stance stops a hit that comes in at a level.
    /// </summary>
    private static bool Covers(Stance stance, HitLevel level) => level switch
    {
        HitLevel.Low => stance == Stance.Crouching,
        HitLevel.Overhead => stance == Stance.Standing,
        _ => stance is Stance.Standing or Stance.Crouching
    };

    /// <summary>
    /// Helper method to work out what a hit takes off. A counter hit hurts more, every hit of a combo after the first hurts
    /// less, and so does a move the attacker has landed a lot lately (stale moves).
    /// </summary>
    /// <param name="hitsBefore">How many hits of the combo landed before this one.</param>
    private static int DamageOf(PlayerController attacker, FightingMove move, bool counter, int hitsBefore)
    {
        float scale = MathF.Max(CombatRules.MIN_COMBO_DAMAGE_SCALE, MathF.Pow(CombatRules.COMBO_DAMAGE_SCALE, hitsBefore));
        if (counter) scale *= CombatRules.COUNTER_DAMAGE_SCALE;
        scale *= attacker.StaleScale(move);

        return Math.Max(1, (int)MathF.Round(move.Damage * scale));
    }

    /// <summary>
    /// Helper method to send whoever got hit where the move says, once the hitlag is over (it is queued, see
    /// PlayerController.QueueLaunch). A launcher sets off so they get exactly launch.y above where they were hit and are
    /// launch.x along by the time they're at the top, on the gravity they fall with in hitstun, further with every 10 health
    /// they have lost (launch_growth) and the direction they hold turns it a little (DI). Anything else slides them push
    /// along the ground (and the attacker back half of that), or in the air pops them up a little and pushes them that far
    /// before the air stops them.
    /// </summary>
    /// <param name="healthLost">How much health they have lost this round, this hit's among it.</param>
    private static void Send(PlayerController attacker, PlayerController victim, FightingMove move, float direction, float gravityShare, int healthLost)
    {
        var numbers = victim.Player.Character.Movement;
        IFighterBody body = victim.Body;

        if (move.Launches)
        {
            Vector2 launch = move.Launch + move.LaunchGrowth * (healthLost / 10.0f) * (100.0f / numbers.Weight);
            float gravity = numbers.Gravity * gravityShare;
            float up = numbers.SpeedToReach(launch.Y, gravityShare);
            float away = launch.X / (up / gravity);
            victim.QueueLaunch(new Vector2(direction * away, up), influenced: true);
            victim.State.ResetFallDuration();
            return;
        }

        float push = move.Push >= 0.0f ? move.Push : CombatRules.PUSH;

        if (!victim.State.IsGrounded)
        {
            float pop = MathF.Max(body.Velocity.Y, numbers.SpeedToReach(CombatRules.AIR_HIT_POP, gravityShare));
            victim.QueueLaunch(new Vector2(direction * MathF.Sqrt(2.0f * numbers.AirFriction * push), pop), influenced: true);
            return;
        }

        victim.QueueLaunch(new Vector2(direction * numbers.SpeedToSlide(push), body.Velocity.Y), influenced: false);

        // The attacker gives a little ground too, never off a launcher, they want to be under whoever they sent up
        IFighterBody attackerBody = attacker.Body;
        if (attacker.State.IsGrounded)
        {
            float recoil = attacker.Player.Character.Movement.SpeedToSlide(push * CombatRules.ATTACKER_RECOIL);
            attacker.QueueLaunch(new Vector2(-direction * recoil, attackerBody.Velocity.Y), influenced: false);
        }
    }

    /// <summary>
    /// Helper method to work out how much of their gravity whoever got hit falls with until their hitstun is over. What
    /// the move says or what their character does, and a little more for every hit of a juggle.
    /// </summary>
    private static float GravityShareOf(PlayerController victim, FightingMove move, bool juggle)
    {
        float share = move.HitstunGravity ?? victim.Player.Character.Movement.HitstunGravity;
        if (!juggle) return share;

        return MathF.Min(MathF.Max(1.0f, share), share + CombatRules.JUGGLE_GRAVITY_STEP * (victim.State.JuggleHits + 1));
    }
}
