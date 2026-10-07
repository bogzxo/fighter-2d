using System;

using Fighter2D.Character.Controllers;

namespace Fighter2D.Networking;

/// <summary>
/// Puts a network player right. We play them by their buttons, which is close but drifts, so every snapshot of theirs is held up against our copy.
/// Wherever the two have come apart their machine wins.
/// </summary>
internal static class PlayerReconciler
{
    // How many frames our copy of their move can be off by and still be left alone
    private const int FRAME_TOLERANCE = 2;

    /// <summary>
    /// Called with a snapshot once our copy has played the buttons of the tick the snapshot is from.
    /// </summary>
    public static void Apply(PlayerController controller, in PlayerSnapshot snapshot)
    {
        controller.Player.Flipped = snapshot.Flipped;
        controller.StateTracker.Restore(snapshot.Status, snapshot.HitstunTicks, snapshot.ComboCount);

        if (!IsInStep(controller, snapshot) && controller.MoveList.TryGetMove(snapshot.MoveId, out var move))
        {
            // Dropped onto the very frame they are on over there. If their move holds that frame then so do we, no more looping animations
            controller.RestoreMove(move, snapshot.Phase, (uint)snapshot.Frame, snapshot.Animation, snapshot.Shown, snapshot.CanCancel, snapshot.HitThrown);
        }

        controller.Movement.CorrectPosition(snapshot.Position, snapshot.Velocity);
    }

    /// <summary>
    /// Helper method to test if our copy is in the same move as theirs and close enough to the same frame of it.
    /// </summary>
    private static bool IsInStep(PlayerController controller, in PlayerSnapshot snapshot) =>
        controller.CurrentMove.Id == snapshot.MoveId
        && controller.Playback.Phase == snapshot.Phase
        && Math.Abs((int)controller.Playback.Frame - snapshot.Frame) <= FRAME_TOLERANCE;
}
