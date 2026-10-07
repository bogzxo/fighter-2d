using System;
using System.Numerics;

namespace Fighter2D.Combat;

/// <summary>
/// The one place that decides whether a hit lands and what it does.
/// Every hit of the game comes through here no matter who threw it (a local player, the dummy, a network player) and who took it.
/// The numbers it uses are in <see cref="CombatRules"/>.
/// </summary>
internal static class HitResolver
{
    /// <summary>
    /// Called by a move on its hit frame. Tests the attacker's hitbox against the opponent's hurtbox and resolves the hit if they touch.
    /// </summary>
    /// <param name="frame">The frame of the animation the hit comes out on, which is what the hitbox is read from.</param>
    public static void TryLand(PlayerController attacker, uint frame)
    {
        // A network player doesn't get to decide anything here, their hits and whiffs arrive as messages (see FightNetwork)
        if (attacker.IsRemote) return;

        Player player = attacker.Player, opponent = attacker.Opponent;
        FightingMove move = attacker.CurrentMove;

        if (!TryConnect(player, opponent, frame, out Box hitbox))
        {
            // Swung at nothing. The other machine can't see that for itself, so it gets told
            player.GainMeter(CombatRules.METER_ON_WHIFF);
            Fight.CombatLog.Report(new AttackReport(player, move, HitResult.Whiff));
            if (opponent.Controller.IsRemote) Fight.Network?.SendWhiff(move.Id);
            return;
        }

        // Where the two boxes meet is where it landed
        hitbox.Overlaps(opponent.Boxes.Hurtbox, out Vector2 impact);
        float direction = opponent.Transform.Position.X < player.Transform.Position.X ? -1 : 1;

        // Online the machine of whoever got hit has the last word. Until it answers we go by what it looked like from here
        bool predicted = opponent.Controller.IsRemote;
        if (predicted) Fight.Network?.SendHit(move.Id, direction, impact);

        Resolve(attacker, opponent.Controller, move, direction, impact, predicted);
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

            case HitResult.Whiff:
                break;

            default:
                lethal = Land(attacker, victim, move, result, direction, predicted);
                break;
        }

        HitFeedback.Play(attacker, victim, move, result, direction, impact, lethal);

        var state = victim.State;
        bool landed = result is not (HitResult.Whiff or HitResult.Blocked);
        Fight.CombatLog.Report(new AttackReport(attacker.Player, move, result, landed ? state.ComboCount : 0, landed ? state.ComboDamage : 0));

        return result;
    }

    /// <summary>
    /// Helper method for a hit that got through. Damage (scaled down the longer the combo), the shove, the hitstun and the meter both sides earn off it.
    /// </summary>
    /// <returns>Whether this was the hit that took the last of the victim's health.</returns>
    private static bool Land(PlayerController attacker, PlayerController victim, FightingMove move, HitResult result, float direction, bool predicted)
    {
        bool counter = result == HitResult.CounterHit;
        int damage = DamageOf(move, counter, victim.State.ComboCount);

        // Going by the health we know of. For a network player that is whatever their last snapshot said
        bool lethal = damage >= victim.Player.Health;

        if (!predicted) victim.Player.Health = (byte)Math.Max(0, victim.Player.Health - damage);

        attacker.Player.GainMeter((int)MathF.Round(damage * CombatRules.METER_PER_DAMAGE_DEALT));
        victim.Player.GainMeter((int)MathF.Round(damage * CombatRules.METER_PER_DAMAGE_TAKEN));

        Shove(victim.Player, move, direction);
        victim.ApplyHitstun(HitstunFor(victim, move, counter), damage, move.Knockdown);

        // A knockdown that doesn't lift them off the ground puts them down right here, the others wait for the landing
        if (move.Knockdown && !move.HasKnockback && victim.State.IsGrounded) victim.KnockDown();

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

        victim.Player.PhysicsBody.ApplyImpulse(new Vector2(direction * CombatRules.BLOCK_PUSHBACK, 0.0f));
        victim.ApplyBlockstun(move.FrameData.Blockstun);
    }

    /// <summary>
    /// Helper method to test if the attacker's hitbox touches the opponent's hurtbox on this frame.
    /// </summary>
    private static bool TryConnect(Player attacker, Player opponent, uint frame, out Box hitbox)
    {
        hitbox = default;

        // You can't hit somebody with your back turned to them
        if (!attacker.IsFacing(opponent)) return false;

        hitbox = attacker.Boxes.Hitbox(frame);
        return attacker.Boxes.HitboxFixture.TestIntersection(opponent.Boxes.HurtboxFixture, attacker.Transform.Position, opponent.Transform.Position);
    }

    /// <summary>
    /// Helper method to decide whether a hit that reached somebody counts, going by the state they are in and where the hit comes in.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    private static HitResult Judge(PlayerController victim, FightingMove move, float direction)
    {
        var state = victim.State;

        // Dodge rolls go straight through hits, and nobody kicks somebody who is lying on the floor
        if (state.CurrentStatus == FighterStatus.Invulnerable || state.IsDowned) return HitResult.Whiff;

        // Blocking only covers the front (a hit from behind travels the same way the victim is facing), and only the
        // level the block is held at: crouched for a low, standing for an overhead, either for a mid
        if (victim.IsBlocking && direction * victim.Player.Facing < 0 && Covers(state.CurrentStance, move.Level)) return HitResult.Blocked;

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
    /// Helper method to work out what a hit takes off. A counter hit hurts more, and every hit of a combo after the first hurts less.
    /// </summary>
    /// <param name="hitsBefore">How many hits of the combo landed before this one.</param>
    private static int DamageOf(FightingMove move, bool counter, int hitsBefore)
    {
        float scale = MathF.Max(CombatRules.MIN_COMBO_DAMAGE_SCALE, MathF.Pow(CombatRules.COMBO_DAMAGE_SCALE, hitsBefore));
        if (counter) scale *= CombatRules.COUNTER_DAMAGE_SCALE;

        return Math.Max(1, (int)MathF.Round(move.Damage * scale));
    }

    /// <summary>
    /// Helper method to shove the victim away from the attacker. Launchers send them into the air, everything else pushes them back a step.
    /// </summary>
    private static void Shove(Player victim, FightingMove move, float direction)
    {
        Vector2 impulse = move.HasKnockback
            ? new Vector2(direction * move.Knockback.X, move.Knockback.Y)
            : new Vector2(direction * CombatRules.HIT_PUSHBACK, 0.0f);

        victim.PhysicsBody.ApplyImpulse(impulse);
    }

    /// <summary>
    /// Helper method to work out how long a hit leaves the victim in hitstun, which depends on whether they were already in it.
    /// </summary>
    private static int HitstunFor(PlayerController victim, FightingMove move, bool counter)
    {
        var state = victim.State;

        int hitstun = move.FrameData.Hitstun;
        if (counter) hitstun = (int)MathF.Round(hitstun * CombatRules.COUNTER_HITSTUN_SCALE);

        // First hit, nothing fancy
        if (!state.IsInHitstun) return hitstun;

        // Juggle. Every hit on somebody who is airborne and in hitstun only adds a little, and less each time
        if (!state.IsGrounded)
        {
            int extension = CombatRules.JUGGLE_EXTENSION - CombatRules.JUGGLE_DECAY * (state.ComboCount - 1);
            return state.HitstunTicks + Math.Max(CombatRules.MIN_JUGGLE_EXTENSION, extension);
        }

        // Combo on the ground. A jab in the middle of a long hitstun doesn't cut it short
        return Math.Max(state.HitstunTicks, hitstun);
    }
}
