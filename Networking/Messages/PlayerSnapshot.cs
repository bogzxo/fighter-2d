using System;
using System.Numerics;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// How a player is really doing, as the machine that plays them says it. Where they are and the exact frame of the move they are in.
/// Sent a few times a second. The other machine plays them by their buttons in between and gets put right by this (see PlayerReconciler).
/// </summary>
/// <param name="Tick">The tick of their fight this was taken on.</param>
/// <param name="Phase">The phase of the move they are in, and <paramref name="Frame"/> how far into it they are (see MovePlayback).</param>
/// <param name="Animation">The animation that is showing, and <paramref name="Shown"/> the frame of it.</param>
/// <param name="HitsSeen">How many of our hits they have heard about, which tells us whether this is from before or after our last one.</param>
internal readonly record struct PlayerSnapshot(
    uint Tick,
    Vector2 Position,
    Vector2 Velocity,
    bool Flipped,
    string MoveId,
    int Phase,
    int Frame,
    string Animation,
    uint Shown,
    FighterStatus Status,
    int HitstunTicks,
    int ComboCount,
    bool CanCancel,
    bool HitThrown,
    byte Health,
    byte HitsSeen)
{
    /// <summary>
    /// Helper method to take down how a local player is doing right now.
    /// </summary>
    public static PlayerSnapshot Of(Player player, byte hitsSeen)
    {
        PlayerController controller = player.Controller;

        return new(
            controller.Tick,
            player.PhysicsBody.Position,
            player.PhysicsBody.Velocity,
            player.Flipped,
            controller.CurrentMove.Id,
            controller.Playback.Phase,
            (int)controller.Playback.Frame,
            controller.ActiveAnimation,
            controller.Playback.Shown,
            controller.State.CurrentStatus,
            controller.State.HitstunTicks,
            controller.State.ComboCount,
            controller.CanCancel,
            controller.HitThrown,
            player.Health,
            hitsSeen);
    }

    public void Write(Message message)
    {
        message.AddUInt(Tick);
        message.AddFloat(Position.X);
        message.AddFloat(Position.Y);
        message.AddFloat(Velocity.X);
        message.AddFloat(Velocity.Y);
        message.AddBool(Flipped);
        message.AddString(MoveId);
        message.AddSByte((sbyte)Math.Clamp(Phase, sbyte.MinValue, sbyte.MaxValue));
        message.AddUShort((ushort)Math.Clamp(Frame, 0, ushort.MaxValue));
        message.AddString(Animation);
        message.AddUShort((ushort)Math.Min(Shown, ushort.MaxValue));
        message.AddByte((byte)Status);
        message.AddUShort((ushort)Math.Clamp(HitstunTicks, 0, ushort.MaxValue));
        message.AddByte((byte)Math.Clamp(ComboCount, 0, byte.MaxValue));
        message.AddBool(CanCancel);
        message.AddBool(HitThrown);
        message.AddByte(Health);
        message.AddByte(HitsSeen);
    }

    public static PlayerSnapshot Read(Message message) => new(
        message.GetUInt(),
        new Vector2(message.GetFloat(), message.GetFloat()),
        new Vector2(message.GetFloat(), message.GetFloat()),
        message.GetBool(),
        message.GetString(),
        message.GetSByte(),
        message.GetUShort(),
        message.GetString(),
        message.GetUShort(),
        (FighterStatus)message.GetByte(),
        message.GetUShort(),
        message.GetByte(),
        message.GetBool(),
        message.GetBool(),
        message.GetByte(),
        message.GetByte());
}
