using System;
using System.Numerics;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Networking;
using Fighter2D.Scenes;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Controller for players driven remotely over the network.
/// Receives snapshots from FightNetwork and updates player position, move state, and animations.
/// </summary>
internal class NetworkedPlayerController : PlayerController
{
    private Vector2 _targetPosition;
    private Vector2 _targetVelocity;
    private bool _hasReceivedState = false;

    /// <summary>
    /// This method is called by FightNetwork for every snapshot the other machine sends of its player.
    /// </summary>
    public void Apply(in PlayerSnapshot snapshot)
    {
        _targetPosition = snapshot.Position;
        _targetVelocity = snapshot.Velocity;
        _hasReceivedState = true;

        Player.Flipped = snapshot.Flipped;

        // Sync stance and status
        StateTracker.CurrentStance = snapshot.Stance;
        StateTracker.CurrentStatus = snapshot.Status;
        Player.AnimationManager.SetFrame(snapshot.Animation, snapshot.Frame);

        // Sync move if changed
        if (CurrentMove.Id != snapshot.MoveId && snapshot.MoveId.Length > 0)
        {
            ChangeToMove(snapshot.MoveId, forceRestart: true);
        }

        // Sync animation if name differs
        if (!string.IsNullOrEmpty(snapshot.Animation) && ActiveAnimation != snapshot.Animation)
        {
            PlayAnimation(snapshot.Animation);
        }
    }

    public override void UpdatePhysics(float dt)
    {
        if (!_hasReceivedState) return;

        // Smoothly interpolate position for rendering and physics alignment
        if (Player.PhysicsBody != null)
        {
            Vector2 currentPos = Player.PhysicsBody.Position;
            float distSq = Vector2.DistanceSquared(currentPos, _targetPosition);

            // Snap if distance is massive (lag spike or spawn teleports)
            if (distSq > 400f * 400f)
            {
                Player.PhysicsBody.Position = _targetPosition;
            }
            else
            {
                Player.PhysicsBody.Position = Vector2.Lerp(currentPos, _targetPosition, MathF.Min(1.0f, dt * 20.0f));
            }
        }
    }

    // Remote player inputs are processed over the network stream, nothing to do locally!
}