using System;
using System.Numerics;

using Horizon.Physics;

namespace Fighter2D.Fighters.Control;

/// <summary>
/// Everything about where a player is and which way they face. That is turning around, walking, drifting in the air,
/// jumping, fast falling, landing, and nudging a network player back to where they belong. The numbers are the character's
/// (see <see cref="CharacterMovement"/>), how much of them a move allows is the move's (walk and drift).
/// Want to change how facing works for a move? This is the only place a player gets turned around by their buttons.
/// </summary>
internal sealed class PlayerMovement(PlayerController controller)
{
    // How long (in seconds) a fall has to last to kick up dust, and to count as a heavy landing
    private const float DUST_FALL_TIME = 0.05f;
    private const float HEAVY_FALL_TIME = 0.2f;

    // How far off (in world units) a network player can be before we bother fixing it, and how far before we stop being polite and teleport them
    private const float CORRECTION_DEAD_ZONE = 3.0f;
    private const float CORRECTION_SNAP_DISTANCE = 96.0f;

    // How fast a network player is eased over to where they belong, higher is quicker
    private const float CORRECTION_EASE = 12.0f;

    // How far a network player still is from where their machine has them
    private Vector2 _positionError;

    // How hard the world pulls, which the gravity of a character is a share of
    private float _worldGravity;

    private Player Player => controller.Player;
    private CharacterMovement Numbers => Player.Character.Movement;

    /// <summary>
    /// Turns the player to face the way they are steering, if the move they are in lets them.
    /// </summary>
    public void UpdateFacing()
    {
        if (!controller.CanTurn) return;

        float direction = controller.Inputs.Steering;
        if (direction != 0) Player.Flipped = direction < 0;
    }

    /// <summary>
    /// Called every physics step. Walks or drifts the player, has them fall with their own gravity (less of it in
    /// hitstun), and eases a network player over to where they should be.
    /// </summary>
    public void UpdatePhysics(float dt)
    {
        if (Player?.PhysicsBody is not { } body) return;

        if (_positionError != Vector2.Zero)
        {
            // Eased over rather than teleported, a player that jumps about looks worse than one that is a little off
            Vector2 step = _positionError * MathF.Min(1.0f, dt * CORRECTION_EASE);
            body.Position += step;
            _positionError -= step;
        }

        var state = controller.State;
        if (state is null) return;

        // Frozen in a hitlag, nothing moves but an SDI shove, which goes through the world so nobody ends up in a wall
        if (controller.IsFrozen)
        {
            body.GravityScale = 0.0f;
            body.SetVelocity(controller.TakeSdiShove() / MathF.Max(dt, 1e-4f));
            return;
        }

        UpdateGravity();

        float vx = Steer(body, state, out float rate);
        body.SetVelocity(new Vector2(MoveTowards(body.Velocity.X, vx, rate * dt), body.Velocity.Y));
    }

    /// <summary>
    /// Gives the body the gravity of the character (the share of it they fall with in hitstun) and how fast it falls at
    /// most. Every physics step, and straight away when a hit or a jump changes it, a step late is a launch short.
    /// </summary>
    public void UpdateGravity()
    {
        if (Player?.PhysicsBody is not { } body || controller.State is not { } state) return;

        if (_worldGravity <= 0.0f) _worldGravity = Player.Parent?.GetComponent<PhysicsWorld>()?.Gravity.Length() ?? 0.0f;
        if (_worldGravity <= 0.0f) return;

        body.GravityScale = Numbers.Gravity * state.GravityShare / _worldGravity;

        // Somebody sent flying falls as fast as a fast fall at most, which is as fast as anybody goes
        body.MaxFallSpeed = state.IsInHitstun || state.FastFalling ? Numbers.FastFallSpeed : Numbers.FallSpeed;
    }

    /// <summary>
    /// Helper method to work out the speed the player is heading for along the ground or the air, and how quickly.
    /// </summary>
    private float Steer(PhysicsBodyComponent2D body, FighterState state, out float rate)
    {
        var numbers = Numbers;
        var move = controller.CurrentMove;

        // The winner of a round walks themselves over to the loser, see VictoryTaunt
        bool strolling = controller.Taunt.IsWalking;
        float direction = strolling ? controller.Taunt.Steering : controller.CanSteer ? controller.Inputs.Steering : 0;
        float pace = strolling ? controller.Taunt.WalkScale : controller.Input.WalkSpeedScale;

        // Sent up off the ground this tick isn't on the ground, whatever the feet said at the start of it, or the traction
        // eats a launch before it gets going
        if (state.IsGrounded && body.Velocity.Y <= 0.0f)
        {
            float share = strolling ? pace : move.Walk * pace;
            if (direction != 0 && share > 0)
            {
                rate = numbers.GroundAcceleration;
                return direction * numbers.WalkSpeed * share;
            }

            // Nothing held, or a move that plants them, and the traction stops them. That is also what eats a push or a slide
            rate = numbers.Traction;
            return 0.0f;
        }

        if (state.IsInHitstun)
        {
            // A launch goes exactly as far as it says on the way up, the air only gets a say on the way down
            if (body.Velocity.Y > 0.0f)
            {
                rate = 0.0f;
                return body.Velocity.X;
            }

            rate = numbers.AirFriction;
            return 0.0f;
        }

        float drift = strolling ? pace : move.Drift * pace;
        if (direction != 0 && drift > 0)
        {
            float target = direction * numbers.AirSpeed * drift;

            // Going that way quicker than drifting would (a running jump, a shove) is only slowed by the air
            bool faster = MathF.Sign(body.Velocity.X) == MathF.Sign(direction) && MathF.Abs(body.Velocity.X) > MathF.Abs(target);
            rate = faster ? numbers.AirFriction : numbers.AirAcceleration;
            return target;
        }

        rate = numbers.AirFriction;
        return 0.0f;
    }

    private static float MoveTowards(float from, float to, float by) =>
        from < to ? MathF.Min(to, from + by) : MathF.Max(to, from - by);

    /// <summary>
    /// Jumps, as high as the character's numbers say for the kind of jump it is. What they were doing sideways carries on.
    /// </summary>
    public void Jump(JumpKind kind)
    {
        var numbers = Numbers;
        float height = kind switch
        {
            JumpKind.Short => numbers.ShortHopHeight,
            JumpKind.Double => numbers.DoubleJumpHeight,
            _ => numbers.JumpHeight
        };

        var body = Player.PhysicsBody;
        body.SetVelocity(new Vector2(body.Velocity.X, numbers.SpeedToReach(height)));

        var state = controller.State;
        state.ResetFallDuration();
        state.FastFalling = false;
        UpdateGravity();
        if (kind == JumpKind.Double) state.AirJumpsLeft = Math.Max(0, state.AirJumpsLeft - 1);

        // The press that got us off the ground isn't the one that jumps again in the air
        controller.Inputs.Buffer.Consume(InputFlags.DPadUp);
    }

    /// <summary>
    /// Called every tick, fast falls if down was pressed in the air on the way down.
    /// </summary>
    public void CheckFastFall()
    {
        var state = controller.State;
        if (state.IsGrounded || state.FastFalling || !controller.IsInControl) return;

        var body = Player.PhysicsBody;
        if (body.Velocity.Y > 0.0f || !controller.Inputs.Buffer.WasPressed(InputFlags.DPadDown)) return;

        state.FastFalling = true;
        body.SetVelocity(new Vector2(body.Velocity.X, -Numbers.FastFallSpeed));
    }

    /// <summary>
    /// Helper method to test if the player just landed, and what that lands them in, the landing lag of an aerial, a
    /// knockdown, or a heavy landing after a long fall.
    /// </summary>
    public void CheckLanding()
    {
        var state = controller.State;
        if (!state.JustLanded) return;

        // Even a short drop throws up a bit of dust
        if (state.FallDuration > DUST_FALL_TIME)
        {
            Fight.Effects.Land(Player.FeetPosition, state.FallDuration);
        }

        // Launched by something that knocks down, this is where they hit the floor and stay there for a bit. Hitting the deck
        // kicks up as much dust as a long fall would, however short the way down was. Unless they teched it
        if (state.KnockdownPending && !controller.IsInControl)
        {
            if (controller.TryTech())
            {
                Fight.Effects.Land(Player.FeetPosition, state.FallDuration);
                state.ResetFallDuration();
                return;
            }

            Fight.Effects.Land(Player.FeetPosition, MathF.Max(state.FallDuration, HEAVY_FALL_TIME));
            controller.KnockDown();
            state.ResetFallDuration();
            return;
        }

        // Came down in the middle of an aerial that costs to land in, Smash's landing lag
        var move = controller.CurrentMove;
        if (controller.IsInControl && move.LandingLag > 0 && state.CurrentStatus == FighterStatus.Attacking
            && controller.MoveList.Moves.ContainsKey(MoveIds.LANDING))
        {
            state.StartLandingLag(move.LandingLag);
            controller.ChangeToMove(MoveIds.LANDING, forceRestart: true);
            state.ResetFallDuration();
            return;
        }

        // Somebody who comes down in hitstun stays in hitstun
        if (state.FallDuration > HEAVY_FALL_TIME && controller.IsInControl)
        {
            controller.ChangeToMove(MoveIds.HEAVY_LAND, forceRestart: true);
        }

        state.ResetFallDuration();
    }

    /// <summary>
    /// Called for a network player with where their own machine has them. Small errors get eased away, big ones get snapped.
    /// </summary>
    public void CorrectPosition(Vector2 position, Vector2 velocity)
    {
        var body = Player.PhysicsBody;
        Vector2 error = position - body.Position;

        if (error.LengthSquared() > CORRECTION_SNAP_DISTANCE * CORRECTION_SNAP_DISTANCE)
        {
            // Too far off to walk it back (lag spike, new round)
            body.Position = position;
            body.SetVelocity(velocity);
            _positionError = Vector2.Zero;

            // ...and not shown getting there either
            Player.Transform.Snap();
            return;
        }

        _positionError = error.LengthSquared() > CORRECTION_DEAD_ZONE * CORRECTION_DEAD_ZONE ? error : Vector2.Zero;
    }

    public void Reset() => _positionError = Vector2.Zero;
}
