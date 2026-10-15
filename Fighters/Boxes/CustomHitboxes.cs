using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

using Horizon.HIDL.Runtime;

namespace Fighter2D.Fighters;

/// <summary>
/// The hitbox somebody drew by hand for a move, in place of the one traced off the art. A box for every active frame of
/// the move, the first on the hit frame and one more for every frame it stays out after that, so a kick can reach
/// further on its second frame than its first. Where they are is a share of the frame, see <see cref="FrameBoxes"/>.
/// </summary>
internal readonly record struct CustomHitbox(Box[] Boxes)
{
    /// <summary>How many frames of the move it's out for from the hit frame, 1 is the hit frame and nothing more.</summary>
    public int Active => Boxes.Length;

    /// <summary>The same box on every one of so many active frames.</summary>
    public static CustomHitbox Of(Box box, int active) => new([.. Enumerable.Repeat(box, Math.Max(1, active))]);

    /// <summary>The box of an active frame, counted from 0 on the hit frame. Past the last it's the last one.</summary>
    public Box At(int activeFrame) => Boxes[Math.Clamp(activeFrame, 0, Boxes.Length - 1)];

    public CustomHitbox WithBox(int activeFrame, Box box)
    {
        Box[] boxes = [.. Boxes];
        boxes[Math.Clamp(activeFrame, 0, boxes.Length - 1)] = box;
        return new(boxes);
    }

    /// <summary>Out for so many frames. Frames that weren't there before get the box of the last one there was.</summary>
    public CustomHitbox WithActive(int active)
    {
        Box[] boxes = Boxes;
        return new([.. Enumerable.Range(0, Math.Max(1, active)).Select(i => boxes[Math.Min(i, boxes.Length - 1)])]);
    }

    /// <summary>Whether every active frame has the same box, which the file writes the short way.</summary>
    public bool IsSameThroughout => Boxes.Length == 0 || Array.TrueForAll(Boxes, Boxes[0].Equals);

    public bool Equals(CustomHitbox other) => (Boxes ?? []).SequenceEqual(other.Boxes ?? []);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (Box box in Boxes ?? []) hash.Add(box);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The hand drawn hitboxes of a character, one file a character (Assets/data/hitboxes/the_id.hor), made with the
/// move editor (Hex's Move editor extension). A move that isn't in there goes on hitting with whatever limb the art has sticking out
/// (<see cref="SpriteBoxTracer"/>), which is most of them and the way it should be, a box is only drawn by hand where
/// the trace gets it wrong or a move wants more reach, or more frames out, than the art shows.
/// <code>
/// let hitboxes = {
///     kick_left: { min: vec(0.05, -0.12), max: vec(0.32, 0.06), active: 2 },
///     sweep: { frames: [
///         { min: vec(0, -0.5), max: vec(0.4, -0.3) },
///         { min: vec(0, -0.5), max: vec(0.6, -0.3) }
///     ] }
/// };
/// </code>
/// The short way is one box for as many active frames as it says, the long way a box for every one of them.
/// </summary>
internal static class CustomHitboxes
{
    public const string DIRECTORY = "Assets/data/hitboxes";

    /// <summary>Where the hitboxes of a character are, relative to the content.</summary>
    public static string FileOf(string characterId) => $"{DIRECTORY}/{characterId}.hor";

    /// <summary>
    /// The hand drawn hitboxes of a character by move, nothing for one that hasn't got any. Throws for a file that
    /// makes no sense, saying where.
    /// </summary>
    public static Dictionary<string, CustomHitbox> Load(string characterId)
    {
        var read = new Dictionary<string, CustomHitbox>();
        string file = GameContent.PathOf(FileOf(characterId));
        if (!File.Exists(file)) return read;

        foreach (var (move, value) in HorReader.LoadObject(file, "hitboxes"))
        {
            if (value is not ObjectValue { Properties: { } box }) throw new Exception($"'{file}': the hitbox of '{move}' has to be an object.");

            if (box.TryGetValue("frames", out var frames))
            {
                if (frames is not ListValue { Items: { Count: > 0 } items }) throw new Exception($"'{file}': the frames of '{move}' have to be a list of boxes.");

                var boxes = new List<Box>();
                foreach (IRuntimeValue item in items)
                {
                    if (item is not ObjectValue { Properties: { } one }) throw new Exception($"'{file}': a frame of '{move}' has to be a box, {{ min, max }}.");
                    boxes.Add(ReadBox(one, file, move));
                }

                read[move] = new CustomHitbox([.. boxes]);
            }
            else read[move] = CustomHitbox.Of(ReadBox(box, file, move), (int)HorReader.Number(box, "active", 1));
        }

        return read;
    }

    private static Box ReadBox(Dictionary<string, IRuntimeValue> box, string file, string move)
    {
        Vector2 min = HorReader.Vector(box, "min", Vector2.Zero), max = HorReader.Vector(box, "max", Vector2.Zero);
        if (max.X <= min.X || max.Y <= min.Y) throw new Exception($"'{file}': a hitbox of '{move}' has nothing in it, max has to be past min.");
        return new Box(min, max);
    }

    /// <summary>Gives the moves of a list their hand drawn hitboxes, before their frame data is worked out.</summary>
    public static void Apply(Moves.MoveList moves, string characterId)
    {
        foreach (var (id, hitbox) in Load(characterId))
        {
            if (!moves.Moves.TryGetValue(id, out var move)) continue;
            move.CustomHitboxes = hitbox.Boxes;
            move.ActiveFrames = hitbox.Active;
        }
    }

    /// <summary>Writes the hitboxes of a character, the file gone if there are none.</summary>
    public static void Save(string characterId, IReadOnlyDictionary<string, CustomHitbox> hitboxes)
    {
        string file = GameContent.PathOf(FileOf(characterId));
        if (hitboxes.Count == 0)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var text = new StringBuilder();
        text.AppendLine($"// The hand drawn hitboxes of {characterId}, written by the move editor (Hex). A move that isn't here hits");
        text.AppendLine("// with whatever limb its art has sticking out. min and max are a share of the frame from its middle, x the way the");
        text.AppendLine("// character faces and y up, active is how many frames of the move the box stays out for from the hit frame. A move whose");
        text.AppendLine("// box changes from one active frame to the next has frames, a box for every one of them from the hit frame on.");
        text.AppendLine();
        text.AppendLine("let hitboxes = {");

        string[] ids = [.. hitboxes.Keys.OrderBy(id => id, StringComparer.Ordinal)];
        for (int i = 0; i < ids.Length; i++)
        {
            CustomHitbox box = hitboxes[ids[i]];
            if (box.IsSameThroughout) text.Append($"    {ids[i]}: {{ {Write(box.At(0))}, active: {box.Active} }}");
            else
            {
                text.AppendLine($"    {ids[i]}: {{ frames: [");
                for (int f = 0; f < box.Active; f++)
                    text.AppendLine($"        {{ {Write(box.Boxes[f])} }}{(f < box.Active - 1 ? "," : "")}");
                text.Append("    ] }");
            }


            text.AppendLine(i < ids.Length - 1 ? "," : "");
        }

        text.AppendLine("};");
        File.WriteAllText(file, text.ToString());
    }

    private static string Write(Box box) =>
        $"min: vec({Number(box.Min.X)}, {Number(box.Min.Y)}), max: vec({Number(box.Max.X)}, {Number(box.Max.Y)})";

    private static string Number(float value) => MathF.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
