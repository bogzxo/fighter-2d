using System.Text;

using Horizon.HIDL;
using Horizon.HIDL.Runtime;

namespace Fighter2D.HitboxEditor;

/// <summary>
/// Keys and their values in the order they were written, which is the order they go back in. A Dictionary forgets
/// that the moment something is taken out and put back, and the order of the phases of a move is the move.
/// Never changed once made, every edit is a new one, so a copy of the drafts is all undo needs.
/// </summary>
internal sealed class Fields
{
    public static readonly Fields Empty = new([]);

    private readonly KeyValuePair<string, IRuntimeValue>[] entries;

    private Fields(KeyValuePair<string, IRuntimeValue>[] entries) => this.entries = entries;

    public static Fields Of(Dictionary<string, IRuntimeValue> values) => new([.. values]);

    public IEnumerable<KeyValuePair<string, IRuntimeValue>> Entries => entries;

    public bool Has(string key) => Array.FindIndex(entries, e => e.Key == key) >= 0;

    public IRuntimeValue? Get(string key)
    {
        int at = Array.FindIndex(entries, e => e.Key == key);
        return at >= 0 ? entries[at].Value : null;
    }

    /// <summary>The same with a key set, where it was if it was there and at the end if it wasn't. Null takes it out.</summary>
    public Fields With(string key, IRuntimeValue? value)
    {
        int at = Array.FindIndex(entries, e => e.Key == key);
        if (value is null) return at < 0 ? this : new([.. entries.Where((_, i) => i != at)]);

        var copy = entries.ToList();
        if (at >= 0) copy[at] = new(key, value);
        else copy.Add(new(key, value));
        return new([.. copy]);
    }

    public Dictionary<string, IRuntimeValue> ToDictionary()
    {
        var made = new Dictionary<string, IRuntimeValue>();
        foreach (var (key, value) in entries) made[key] = value;
        return made;
    }
}

/// <summary>One phase of a move being edited, its name and what it says.</summary>
internal sealed record PhaseDraft(string Name, Fields Fields);

/// <summary>
/// A move as the editor holds it, what its object in the move file says, key by key. It is read the way the game reads
/// a move (MoveFileReader.ReadOne), so what the editor shows is what a fight would do, and written back as text into
/// the character's own move file.
/// </summary>
internal sealed record MoveDraft(string Id, Fields Fields, PhaseDraft[] Phases)
{
    private const string PHASES = "phases";

    // How long a phase can be on one line before it goes a key a line
    private const int SHORT_LINE = 110;

    public static MoveDraft Of(string id, Dictionary<string, IRuntimeValue> move)
    {
        var phases = new List<PhaseDraft>();
        if (move.TryGetValue(PHASES, out var value) && value is ObjectValue { Properties: { } read })
        {
            foreach (var (name, phase) in read)
            {
                phases.Add(new PhaseDraft(name, phase is ObjectValue { Properties: { } fields } ? Fields.Of(fields) : Fields.Empty));
            }
        }

        // The phases are kept apart, a placeholder stays where they were written so they go back there
        return new MoveDraft(id, Fields.Of(move).With(PHASES, move.ContainsKey(PHASES) ? new NullValue() : null), [.. phases]);
    }

    public MoveDraft With(string key, IRuntimeValue? value) => this with { Fields = Fields.With(key, value) };

    public MoveDraft WithPhases(PhaseDraft[] phases) => this with { Phases = phases };

    public MoveDraft WithPhase(int index, PhaseDraft phase)
    {
        PhaseDraft[] copy = [.. Phases];
        copy[index] = phase;
        return this with { Phases = copy };
    }

    /// <summary>What the move file would hold for it, as the reader of move files wants it.</summary>
    public Dictionary<string, IRuntimeValue> ToDictionary()
    {
        var made = new Dictionary<string, IRuntimeValue>();
        bool placed = false;
        foreach (var (key, value) in Fields.Entries)
        {
            if (key == PHASES)
            {
                placed = true;
                if (Phases.Length > 0) made[PHASES] = PhasesValue();
            }
            else made[key] = value;
        }

        if (!placed && Phases.Length > 0) made[PHASES] = PhasesValue();
        return made;
    }

    private ObjectValue PhasesValue()
    {
        var phases = new Dictionary<string, IRuntimeValue>();
        foreach (PhaseDraft phase in Phases) phases[phase.Name] = new ObjectValue(phase.Fields.ToDictionary());
        return new ObjectValue(phases);
    }

    /// <summary>
    /// The move as the text of its object in a move file, laid out the way the files are written by hand. A key a line,
    /// a phase a line where it fits on one.
    /// </summary>
    /// <param name="indent">How far in the line the object starts on is, the closing brace goes there.</param>
    public string ToSource(string indent = "    ")
    {
        string inner = indent + "    ";
        var text = new StringBuilder("{\n");
        var lines = new List<string>();

        foreach (var (key, value) in ToDictionary())
        {
            if (key == PHASES)
            {
                var phases = new StringBuilder("phases: {\n");
                for (int i = 0; i < Phases.Length; i++)
                {
                    string line = $"{Key(Phases[i].Name)}: {Inline(Phases[i].Fields.Entries)}";
                    if (inner.Length + 4 + line.Length > SHORT_LINE) line = $"{Key(Phases[i].Name)}: {Block(Phases[i].Fields.Entries, inner + "    ")}";
                    phases.Append(inner).Append("    ").Append(line).Append(i < Phases.Length - 1 ? ",\n" : "\n");
                }

                lines.Add(phases.Append(inner).Append('}').ToString());
            }
            else lines.Add($"{Key(key)}: {Value(value, inner)}");
        }

        for (int i = 0; i < lines.Count; i++) text.Append(inner).Append(lines[i]).Append(i < lines.Count - 1 ? ",\n" : "\n");
        return text.Append(indent).Append('}').ToString();
    }

    private static string Inline(IEnumerable<KeyValuePair<string, IRuntimeValue>> entries)
    {
        string inside = string.Join(", ", entries.Select(e => $"{Key(e.Key)}: {Value(e.Value, "")}"));
        return inside.Length == 0 ? "{}" : $"{{ {inside} }}";
    }

    private static string Block(IEnumerable<KeyValuePair<string, IRuntimeValue>> entries, string indent)
    {
        var all = entries.ToArray();
        var text = new StringBuilder("{\n");
        for (int i = 0; i < all.Length; i++)
            text.Append(indent).Append("    ").Append($"{Key(all[i].Key)}: {Value(all[i].Value, indent + "    ")}").Append(i < all.Length - 1 ? ",\n" : "\n");
        return text.Append(indent).Append('}').ToString();
    }

    private static string Value(IRuntimeValue value, string indent) => value switch
    {
        // Reroutes, which are short, on one line
        ObjectValue { Properties: { } properties } => Inline(properties),
        _ => HIDLWriter.Write(value)
    };

    private static string Key(string key) => HIDLWriter.IsIdentifier(key) ? key : HIDLWriter.Write(new StringValue(key));
}
