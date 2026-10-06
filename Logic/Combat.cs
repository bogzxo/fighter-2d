using System;
using System.Numerics;

using Fighter2D.Character.Controllers;
using Fighter2D.Logic.Moves;
using Fighter2D.Scenes;

using Horizon.Rendering.Spriting;

namespace Fighter2D.Logic;

/// <summary>
/// What became of a blow that reached somebody.
/// </summary>
internal enum HitOutcome
{
    // It went straight through them (dodge roll)
    Missed,

    // Their guard was up and facing it
    Blocked,

    Hit,

    // It caught them in the middle of throwing a blow of their own, which costs them more
    Counter
}

/// <summary>
/// Every blow of the game comes through here, whoever threw it (a player of this machine, the dummy, a player of another machine) and whoever
/// took it, so what a hit comes to is decided in this one place.
/// All of the times in here are in ticks of the fight, see PlayerConfig.TICK_RATE.
/// </summary>
internal static class Combat
{
    /* How long a hit stuns for when its move doesn't say (see MoveFrameData): the move is over for the attacker
       BASE_DISADVANTAGE ticks after whoever they hit is free again, less WINDUP_REWARD ticks for every tick the blow took
       to come out. Quick blows leave the attacker behind, which is the opening a faster move of the other player gets
       through. Slow ones leave them ahead, which is the opening for a quick one to follow up in. */
    public const float BASE_DISADVANTAGE = 6.0f;
    public const float WINDUP_REWARD = 0.75f;
    public const float MAX_ADVANTAGE = 24.0f;
    public const int MIN_HIT_STUN = 4;

    // What a blow comes to when it catches somebody winding up one of their own
    private const float COUNTER_STUN = 1.5f;
    private const float COUNTER_DAMAGE = 1.25f;

    // How much longer a blow keeps somebody stunned who is already stunned and in the air. Every one of them adds less,
    // down to the least of it: nobody is kept up there for good
    private const int JUGGLE_EXTENSION = 10, JUGGLE_DECAY = 2, MIN_JUGGLE_EXTENSION = 2;

    // For how long a blow holds both fighters on the frame it landed on
    private const int HIT_STOP = 4, HEAVY_HIT_STOP = 8, COUNTER_HIT_STOP = 11, BLOCK_STOP = 2;

    // For how long (in seconds) whoever took it is lit up by it
    private const float FLASH_TIME = 0.22f, COUNTER_FLASH_TIME = 0.4f;

    // What a blow turns whoever took it for a moment: drawn brighter than they are, which reads as white
    private static readonly Vector4 HitFlash = new(2.6f, 2.4f, 2.4f, 1.0f);
    private static readonly Vector4 CounterFlash = new(3.2f, 2.2f, 0.9f, 1.0f);
    private static readonly Vector4 BlockFlash = new(1.4f, 1.6f, 2.0f, 1.0f);

    /// <summary>
    /// Called when a blow of a player of this machine reaches the other player.
    /// </summary>
    /// <param name="direction">The way the blow was going, -1 for left and 1 for right.</param>
    /// <param name="impact">Where it landed.</param>
    public static HitOutcome Land(PlayerController attacker, PlayerController victim, FightingMove move, float direction, Vector2 impact)
    {
        // What comes of it is up to the machine that plays whoever got hit. Until it says, they are taken to have fared
        // the way it looks from here
        bool predicted = victim.IsRemote;
        if (predicted) FightScene.Network?.SendHit(move.Id, direction, impact);

        return Resolve(attacker, victim, move, direction, impact, predicted);
    }

    /// <summary>
    /// Decides what a blow comes to and does it.
    /// </summary>
    /// <param name="predicted">Whether this is only what the blow looks like from here, with the last word on it
    /// (and on the health of whoever took it) had on another machine.</param>
    public static HitOutcome Resolve(PlayerController attacker, PlayerController victim, FightingMove move, float direction, Vector2 impact, bool predicted)
    {
        HitOutcome outcome = Judge(victim, direction);
        bool launches = move.Knockback != Vector2.Zero;

        switch (outcome)
        {
            case HitOutcome.Missed:
                break;

            case HitOutcome.Blocked:
                FightScene.Effects.Block(impact, direction);
                Jolt(attacker, victim, BLOCK_STOP, BlockFlash, FLASH_TIME);
                break;

            default:
                bool counter = outcome == HitOutcome.Counter;

                Hurt(victim, move, direction, counter, predicted);
                FightScene.Effects.Hit(impact, direction, heavy: launches || counter);
                Jolt(attacker, victim,
                    counter ? COUNTER_HIT_STOP : launches ? HEAVY_HIT_STOP : HIT_STOP,
                    counter ? CounterFlash : HitFlash,
                    counter ? COUNTER_FLASH_TIME : FLASH_TIME);
                break;
        }

        return outcome;
    }

    /// <summary>
    /// Helper method to decide whether a blow that reached somebody counts, going by nothing but the state they are in.
    /// </summary>
    /// <param name="direction">The way the blow was going, -1 for left and 1 for right.</param>
    public static HitOutcome Judge(PlayerController victim, float direction)
    {
        // Dodge rolls go straight through hits
        if (victim.StateTracker.CurrentStatus == PlayerStatusType.Invulnerable) return HitOutcome.Missed;

        // A guard only covers the side it is held up to: a blow that comes from behind goes the way we are facing
        if (victim.IsGuarding && direction * victim.Player.Facing < 0) return HitOutcome.Blocked;

        // Caught winding up a blow of their own that the other one beat them to
        if (victim.IsCommitted) return HitOutcome.Counter;

        return HitOutcome.Hit;
    }

    /// <summary>
    /// Helper method for what a blow that counts does to whoever took it: the damage, the push and the stun.
    /// </summary>
    private static void Hurt(PlayerController victim, FightingMove move, float direction, bool counter, bool predicted)
    {
        var player = victim.Player;
        var state = victim.StateTracker;

        // Their health is kept by the machine that plays them
        if (!predicted)
        {
            float damage = move.Damage * (counter ? COUNTER_DAMAGE : 1.0f);
            player.Health = (byte)Math.Max(0, player.Health - (int)MathF.Round(damage));
        }

        // Launch them away from the attacker (jump kicks send them into the air)
        if (move.Knockback != Vector2.Zero)
        {
            player.PhysicsBody.ApplyImpulse(new Vector2(direction * move.Knockback.X, move.Knockback.Y));
        }

        int stun = move.FrameData.HitStun;
        if (counter) stun = (int)MathF.Round(stun * COUNTER_STUN);

        if (state.IsStunned && !state.IsGrounded)
        {
            // Kept in the air: whatever hits them up there only adds a little to the stun they are already in
            stun = state.StunTicks + Math.Max(MIN_JUGGLE_EXTENSION, JUGGLE_EXTENSION - JUGGLE_DECAY * (state.ComboHits - 1));
        }
        else if (state.IsStunned)
        {
            // A blow in the middle of a stun doesn't cut it short, a quick one after a slow one leaves the slow one's standing
            stun = Math.Max(state.StunTicks, stun);
        }

        victim.Stun(stun);
    }

    /// <summary>
    /// Helper method to let a blow be felt: both fighters hang on the frame it landed on for a moment, and whoever took it flashes.
    /// What it does to them (damage, knockback) is none of this, only how it looks.
    /// </summary>
    private static void Jolt(PlayerController attacker, PlayerController victim, int stop, Vector4 flash, float flashTime)
    {
        attacker.Freeze(stop);
        victim.Freeze(stop);

        victim.Player.Tint = flash;
        victim.Player.TweenTint(Vector4.One, flashTime);
    }
}
