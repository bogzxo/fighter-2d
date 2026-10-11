using System.Text;
using System.Text.RegularExpressions;

using Fighter2D.Content;
using Fighter2D.Fighters;
using Fighter2D.Moves;

using Horizon.HIDL.Runtime;

namespace Fighter2D.HitboxEditor;

/// <summary>
/// Where the moves of a character come from and where an edited one goes. A character lists the files of its moves in
/// characters.hor, a move in a later file replacing the one of the same name in an earlier one, and the last of them
/// is its own (the_man_moves.hor, cammy_moves.hor), the others being shared with everybody. An edited move goes in
/// the own file, so a change to a move every character shares is a change for that character alone, the same way
/// the own files were written by hand.
/// <para>
/// Writing the own file only touches the moves that changed. The text of every other move is left the way it was, with
/// its comments, which a move that is written anew loses (the ones inside it, the ones over it stay).
/// </para>
/// </summary>
internal sealed partial class MoveSource
{
    public CharacterDefinition Character { get; }

    /// <summary>The character's own move file, where it is in the content.</summary>
    public string OwnFile => Character.MoveFiles[^1];

    /// <summary>Every move of the character as the files have it, the one that wins of each name.</summary>
    public Dictionary<string, MoveDraft> Moves { get; } = [];

    /// <summary>The moves the own file has.</summary>
    public HashSet<string> Own { get; } = [];

    /// <summary>What the files before the own one say a move is, which is what it goes back to when the own file lets go of it.</summary>
    public Dictionary<string, MoveDraft> Shared { get; } = [];

    private MoveSource(CharacterDefinition character) => Character = character;

    /// <summary>Reads every move file of a character. Throws with the file and the move if one is broken, the same as the game.</summary>
    public static MoveSource Load(CharacterDefinition character)
    {
        var source = new MoveSource(character);
        for (int i = 0; i < character.MoveFiles.Length; i++)
        {
            bool own = i == character.MoveFiles.Length - 1;
            foreach (var (id, value) in HorReader.LoadObject(GameContent.PathOf(character.MoveFiles[i]), "moves"))
            {
                if (value is not ObjectValue { Properties: { } move }) throw new Exception($"'{character.MoveFiles[i]}': the move '{id}' has to be an object.");

                MoveDraft draft = MoveDraft.Of(id, move);
                source.Moves[id] = draft;
                if (own) source.Own.Add(id);
                else source.Shared[id] = draft;
            }
        }

        return source;
    }

    /// <summary>
    /// Writes the own file so it holds these moves, as these drafts, and nothing else of its own. A move whose draft is
    /// what the file has already is left alone. The new file is read the way the game reads it before it goes anywhere
    /// near the old one, a broken move file never gets written.
    /// </summary>
    /// <returns>How many moves of the file were written, added or taken out.</returns>
    public int Save(IReadOnlyDictionary<string, MoveDraft> moves, IReadOnlySet<string> own)
    {
        string path = GameContent.PathOf(OwnFile);
        string text = File.ReadAllText(path);
        var (entries, open) = Scan(text, OwnFile);

        var edits = new List<(int Start, int End, string With)>();
        var added = new StringBuilder();
        int changed = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (!own.Contains(entry.Id))
            {
                // Out goes every move in a row that is going, with the comma before them and whatever was written over
                // them. At the top of the file there is no comma before them, so the one after them goes instead
                int last = i;
                while (last + 1 < entries.Count && !own.Contains(entries[last + 1].Id)) last++;

                int start = i > 0 ? entries[i - 1].End : entry.Start;
                int end = i > 0 ? entries[last].End : SkipComma(text, entries[last].End);
                edits.Add((start, end, ""));
                changed += last - i + 1;
                i = last;
            }
            else if (moves.TryGetValue(entry.Id, out var draft) && Moves.TryGetValue(entry.Id, out var was) && draft.ToSource() != was.ToSource())
            {
                edits.Add((entry.ValueStart, entry.End, draft.ToSource()));
                changed++;
            }
        }

        foreach (string id in own)
        {
            if (entries.Exists(e => e.Id == id) || !moves.TryGetValue(id, out var draft)) continue;
            added.Append(",\n\n    ").Append(id).Append(": ").Append(draft.ToSource());
            changed++;
        }

        if (added.Length > 0)
        {
            // After the last move that stays, or straight into the object when none does
            int kept = entries.FindLastIndex(e => own.Contains(e.Id));
            if (kept >= 0) edits.Add((entries[kept].End, entries[kept].End, added.ToString()));
            else edits.Add((open + 1, open + 1, added.ToString()[1..] + "\n"));
        }

        if (changed == 0) return 0;

        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var written = new StringBuilder(text);
        foreach (var (start, end, with) in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.End))
        {
            // In whatever line endings the file has, a file of CRLFs with a move of LFs in the middle is a mess in a diff
            written.Remove(start, end - start).Insert(start, newline == "\n" ? with : with.Replace("\n", newline));
        }

        string result = written.ToString();
        Check(result);
        File.WriteAllText(path, result);
        return changed;
    }

    /// <summary>Reads every move file of the character with the own one swapped for a new text of it, throwing if any of it is wrong.</summary>
    private void Check(string ownText)
    {
        string scratch = Path.Combine(Path.GetTempPath(), $"hitbox-editor-{Guid.NewGuid():N}.hor");
        try
        {
            File.WriteAllText(scratch, ownText);
            MoveList.Load([.. Character.MoveFiles[..^1].Select(GameContent.PathOf), scratch]);
        }
        catch (Exception e)
        {
            throw new Exception(e.Message.Replace(scratch, OwnFile));
        }
        finally
        {
            File.Delete(scratch);
        }
    }

    /* Finding the moves in the text of a file */

    private readonly record struct Entry(string Id, int Start, int ValueStart, int End);

    [GeneratedRegex(@"\blet\s+moves\s*=\s*\{")]
    private static partial Regex MovesDeclaration();

    /// <summary>Where every move of the moves object of a file starts and ends, and where the object opens.</summary>
    private static (List<Entry> Entries, int Open) Scan(string text, string file)
    {
        System.Text.RegularExpressions.Match found = MovesDeclaration().Match(text);
        if (!found.Success) throw new Exception($"'{file}' has no 'let moves = {{' in it.");

        int open = found.Index + found.Length - 1;
        var entries = new List<Entry>();
        int i = open + 1;

        while (true)
        {
            i = SkipSpace(text, i);
            if (i >= text.Length) throw new Exception($"'{file}' never closes its moves.");
            if (text[i] == '}') return (entries, open);

            int start = i;
            string id;
            if (text[i] is '"' or '\'')
            {
                int end = SkipString(text, i);
                id = text[(i + 1)..(end - 1)];
                i = end;
            }
            else
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                id = text[start..i];
            }

            i = SkipSpace(text, i);
            if (id.Length == 0 || i >= text.Length || text[i] != ':') throw new Exception($"'{file}' has something in its moves that isn't a move, around character {start}.");

            i = SkipSpace(text, i + 1);
            if (i >= text.Length || text[i] != '{') throw new Exception($"'{file}': the move '{id}' has to be an object.");

            int valueStart = i;
            i = SkipBrackets(text, i);
            entries.Add(new Entry(id, start, valueStart, i));

            i = SkipSpace(text, i);
            if (i < text.Length && text[i] == ',') i++;
        }
    }

    /// <summary>Past the comma after a move and the blank lines after that, but not the comments of the next one.</summary>
    private static int SkipComma(string text, int i)
    {
        int after = SkipSpace(text, i);
        if (after >= text.Length || text[after] != ',') return i;

        after++;
        while (after < text.Length && char.IsWhiteSpace(text[after])) after++;
        return after;
    }

    private static int SkipSpace(string text, int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) i++;
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
            }
            else break;
        }

        return i;
    }

    private static int SkipString(string text, int i)
    {
        char quote = text[i++];
        while (i < text.Length && text[i] != quote) i += text[i] == '\\' ? 2 : 1;
        return Math.Min(text.Length, i + 1);
    }

    /// <summary>From an opening bracket to just past the one that closes it, strings and comments in between not counting.</summary>
    private static int SkipBrackets(string text, int i)
    {
        int depth = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                i = SkipString(text, i);
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
            {
                i = SkipSpace(text, i);
                continue;
            }

            if (c is '{' or '[' or '(') depth++;
            else if (c is '}' or ']' or ')' && --depth == 0) return i + 1;
            i++;
        }

        throw new Exception("a bracket is never closed.");
    }
}
