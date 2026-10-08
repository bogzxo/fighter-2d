using System;
using System.Numerics;

namespace Fighter2D.Fighters.Control;

/// <summary>
/// Everything about where a player is and which way they face. That is turning around, walking, landing, and nudging a network player back to where they belong.
/// Want to change how facing works for a move? This is the only place a player gets turned around by their buttons.
/// </summary>
internal sealed class PlayerMovement(PlayerController controller)
{
    // How hard the walk force chases the walk speed, higher gets there quicker
    private const float WALK_ACCELERATION = 5f;

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

    private Player Player => controller.Player;

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
    /// Called every physics step to walk the player, and to ease a network player over to where they should be.
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

        // The winner of a round walks themselves over to the loser, see VictoryTaunt
        bool strolling = controller.Taunt.IsWalking;

        float direction = strolling ? controller.Taunt.Steering : controller.CanSteer ? controller.Inputs.Steering : 0;
        if (direction == 0) return;

        float pace = strolling ? controller.Taunt.WalkScale : controller.Input.WalkSpeedScale * controller.CurrentMove.WalkScale;
        float targetVelocity = direction * Player.Character.WalkSpeed * pace;
        float velocityDiff = targetVelocity - body.Velocity.X;

        body.ApplyForce(new Vector2(velocityDiff * body.Mass * WALK_ACCELERATION, 0));
    }

    /// <summary>
    /// Helper method to test if the player just landed, and if the fall was long enough to put them in the heavy landing move.
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
        // kicks up as much dust as a long fall would, however short the way down was
        if (state.KnockdownPending && !controller.IsInControl)
        {
            Fight.Effects.Land(Player.FeetPosition, MathF.Max(state.FallDuration, HEAVY_FALL_TIME));
            controller.KnockDown();
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
