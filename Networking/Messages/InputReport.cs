using System;

using Fighter2D.Character.Controllers;
using Fighter2D.Logic;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// The buttons a player held on the last few ticks, as the machine that plays them says it.
/// Sent every tick, and each one repeats the ones before it so a lost message is covered by the next.
/// </summary>
/// <param name="Tick">The tick the first entry was held on, each entry after it is one tick older.</param>
internal readonly record struct InputReport(uint Tick, InputFlags[] NewestFirst)
{
    // How many ticks every message carries, which is how many messages in a row can get lost without losing a press
    public const int TICKS = 8;

    /// <summary>
    /// Helper method to take down what a local player held up to the tick they are on.
    /// </summary>
    public static InputReport Of(PlayerController controller)
    {
        var held = new InputFlags[Math.Min(TICKS, controller.Tick)];
        for (uint i = 0; i < held.Length; i++) held[i] = controller.Inputs.At(controller.Tick - i);

        return new InputReport(controller.Tick, held);
    }

    public void Write(Message message)
    {
        message.AddUInt(Tick);
        message.AddByte((byte)NewestFirst.Length);
        foreach (InputFlags held in NewestFirst) message.AddUShort((ushort)held);
    }

    public static InputReport Read(Message message)
    {
        uint tick = message.GetUInt();

        var held = new InputFlags[message.GetByte()];
        for (int i = 0; i < held.Length; i++) held[i] = (InputFlags)message.GetUShort();

        return new InputReport(tick, held);
    }
}
