using System;
using System.Numerics;

using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;

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
        HitResult result = Judge(victim, direction);
        bool landed = result is not (HitResult.Whiff or HitResult.Blocked);
        bool lethal = false;

        if (landed)
        {
            bool counter = result == HitResult.CounterHit;
            int damage = DamageOf(move, counter);

            // Going by the health we know of. For a network player that is whatever their last snapshot said
            lethal = damage >= victim.Player.Health;

            if (!predicted) victim.Player.Health = (byte)Math.Max(0, victim.Player.Health - damage);
            Launch(victim.Player, move, direction);
            victim.ApplyHitstun(HitstunFor(victim, move, counter), damage);
        }

        HitFeedback.Play(attacker, victim, move, result, direction, impact, lethal);

        var state = victim.StateTracker;
        Fight.CombatLog.Report(new AttackReport(attacker.Player, move, result, landed ? state.ComboCount : 0, landed ? state.ComboDamage : 0));

        return result;
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
    /// Helper method to decide whether a hit that reached somebody counts, going by nothing but the state they are in.
    /// </summary>
    /// <param name="direction">The way the hit was going, -1 for left and 1 for right.</param>
    private static HitResult Judge(PlayerController victim, float direction)
    {
        // Dodge rolls go straight through hits
        if (victim.StateTracker.CurrentStatus == PlayerStatusType.Invulnerable) return HitResult.Whiff;

        // Blocking only covers the front. A hit from behind travels the same way the victim is facing
        if (victim.IsBlocking && direction * victim.Player.Facing < 0) return HitResult.Blocked;

        // Caught winding up their own attack, should have blocked
        if (victim.IsInStartup) return HitResult.CounterHit;

        // Caught after their own attack already came out, that is what you get for swinging at nothing
        if (victim.IsRecovering) return HitResult.Punish;

        return HitResult.Hit;
    }

    private static int DamageOf(FightingMove move, bool counter) =>
        (int)MathF.Round(move.Damage * (counter ? CombatRules.COUNTER_DAMAGE_SCALE : 1.0f));

    /// <summary>
    /// Helper method to shove the victim away from the attacker, launchers send them into the air.
    /// </summary>
    private static void Launch(Player victim, FightingMove move, float direction)
    {
        if (!move.HasKnockback) return;

        victim.PhysicsBody.ApplyImpulse(new Vector2(direction * move.Knockback.X, move.Knockback.Y));
    }

    /// <summary>
    /// Helper method to work out how long a hit leaves the victim in hitstun, which depends on whether they were already in it.
    /// </summary>
    private static int HitstunFor(PlayerController victim, FightingMove move, bool counter)
    {
        var state = victim.StateTracker;

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
