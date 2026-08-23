using System;
using System.Numerics;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Scenes;
using Silk.NET.Input;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Controller for players driven remotely over the network.
/// Receives snapshots from NetworkingManager and updates player position, move state, and animations.
/// </summary>
internal class NetworkedPlayerController : PlayerController
{
    private Vector2 _targetPosition;
    private Vector2 _targetVelocity;
    private bool _hasReceivedState = false;

    public void ApplyNetworkState(
        Vector2 position,
        Vector2 velocity,
        bool flipped,
        MoveId moveId,
        string animationName,
        Stance stance,
        PlayerStatusType status,
        bool isGrounded)
    {
        _targetPosition = position;
        _targetVelocity = velocity;
        _hasReceivedState = true;

        Player.Flipped = flipped;

        // Sync stance and status
        StateTracker.CurrentStance = stance;
        StateTracker.CurrentStatus = status;

        // Sync move if changed
        if (CurrentMove.Id != moveId && moveId != (MoveId)(-1))
        {
            ChangeToMove(moveId, forceRestart: true);
        }

        // Sync animation if name differs
        if (!string.IsNullOrEmpty(animationName) && ActiveAnimation != animationName)
        {
            PlayAnimation(animationName);
        }
    }

    public override void UpdatePhysics(float dt)
    {
        if (!_hasReceivedState) return;

        // Smoothly interpolate position for rendering and physics alignment
        if (Player.PhysicsBody != null)
        {
            Vector2 currentPos = Player.Transform.Position;
            float distSq = Vector2.DistanceSquared(currentPos, _targetPosition);

            // Snap if distance is massive (lag spike or spawn teleports)
            if (distSq > 400f * 400f)
            {
                Player.Transform.Position = _targetPosition;
            }
            else
            {
                Player.Transform.Position = Vector2.Lerp(currentPos, _targetPosition, MathF.Min(1.0f, dt * 20.0f));
            }
        }
    }

    public override void TryProcessNewInputs(float dt)
    {
        // Remote player inputs are processed over the network stream, nothing to do locally!
    }

    public override bool IsButtonHeld(ButtonName btn)
    {
        return false;
    }
}