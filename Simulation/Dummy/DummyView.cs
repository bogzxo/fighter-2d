using System;
using System.Numerics;

namespace Fighter2D.Fighters.Inputs.Dummy;

/// <summary>
/// What the dummy can see of the fight on one tick, worked out once so every part of its brain goes by the same picture.
/// </summary>
internal readonly struct DummyView
{
    public readonly PlayerController Self, Them;

    // Where the other player is from where we stand
    public readonly Vector2 ToOpponent;
    public readonly float Distance;

    public readonly bool InRange, Grounded, FacingAway;

    public DummyView(PlayerController self)
    {
        Self = self;
        Them = self.Opponent.Controller;

        ToOpponent = self.Opponent.Transform.Position - self.Player.Transform.Position;
        Distance = MathF.Abs(ToOpponent.X);

        InRange = Distance <= DummyConfig.ATTACK_RANGE && MathF.Abs(ToOpponent.Y) <= DummyConfig.ATTACK_RANGE;
        Grounded = self.State.IsGrounded;
        FacingAway = !self.Player.IsFacing(self.Opponent);
    }

    /// <summary>
    /// The direction button that walks us towards the other player.
    /// </summary>
    public InputFlags Towards => ToOpponent.X < 0 ? InputFlags.DPadLeft : InputFlags.DPadRight;

    /// <summary>
    /// Whether an attack thrown right now would actually reach them.
    /// </summary>
    public bool CanReach => InRange && !FacingAway;
}
