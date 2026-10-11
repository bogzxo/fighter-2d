using Fighter2D.Fighters;

namespace Fighter2D.HitboxEditor;

/// <summary>Everything about a character that can be undone, as it was at one moment.</summary>
internal sealed record Snapshot(Dictionary<string, MoveDraft> Moves, HashSet<string> Own, Dictionary<string, CustomHitbox> Hitboxes);

/// <summary>
/// A character as it's being edited. Its moves (as drafts, every one of them, the one that wins of each name), which
/// of them its own move file has, and its hand drawn hitboxes, next to what of all that is on disk.
/// </summary>
internal sealed class Session
{
    public CharacterDefinition Character { get; }

    /// <summary>Where its moves come from, null when they couldn't be read.</summary>
    public MoveSource? Source { get; private set; }

    public Dictionary<string, MoveDraft> Moves { get; private set; } = [];
    public HashSet<string> Own { get; private set; } = [];
    public Dictionary<string, CustomHitbox> Hitboxes { get; private set; } = [];

    // What's on disk, the moves as the text they'd be written as
    private Dictionary<string, string> savedMoves = [];
    private HashSet<string> savedOwn = [];
    private Dictionary<string, CustomHitbox> savedHitboxes = [];

    private Session(CharacterDefinition character) => Character = character;

    /// <summary>Reads the moves and the hitboxes of a character. Throws if its move files are broken.</summary>
    public static Session Load(CharacterDefinition character)
    {
        var session = new Session(character) { Source = MoveSource.Load(character) };
        session.Moves = new(session.Source.Moves);
        session.Own = [.. session.Source.Own];
        session.Hitboxes = CustomHitboxes.Load(character.Id);
        session.MarkSaved();
        return session;
    }

    /// <summary>A character whose moves couldn't be read, nothing in it and nothing that can be saved.</summary>
    public static Session Broken(CharacterDefinition character) => new(character);

    public Snapshot Snapshot() => new(new(Moves), [.. Own], new(Hitboxes));

    public void Restore(Snapshot snapshot)
    {
        Moves = new(snapshot.Moves);
        Own = [.. snapshot.Own];
        Hitboxes = new(snapshot.Hitboxes);
    }

    /// <summary>Whether a move is any different from what's on disk.</summary>
    public bool Changed(string id) =>
        Moves.TryGetValue(id, out var draft) && (!savedMoves.TryGetValue(id, out string? was) || was != draft.ToSource() || Own.Contains(id) != savedOwn.Contains(id));

    private bool MovesUnsaved => Moves.Count != savedMoves.Count || !Own.SetEquals(savedOwn) || Moves.Keys.Any(Changed);

    private bool HitboxesUnsaved => !Hitboxes.OrderBy(e => e.Key).SequenceEqual(savedHitboxes.OrderBy(e => e.Key));

    public bool Unsaved => MovesUnsaved || HitboxesUnsaved;

    /// <summary>
    /// Writes what changed. The moves first, which are checked before anything is written, then the hitboxes, so a move
    /// file that wouldn't read stops the lot.
    /// </summary>
    /// <returns>How many moves and how many hitboxes went to disk.</returns>
    public (int Moves, int Hitboxes) Save()
    {
        if (Source is null) throw new Exception($"{Character.PrettyName}'s moves couldn't be read in the first place, so they can't be written.");

        int written = 0;
        if (MovesUnsaved)
        {
            written = Source.Save(Moves, Own);
            Source = MoveSource.Load(Character);
        }

        int boxes = 0;
        if (HitboxesUnsaved)
        {
            CustomHitboxes.Save(Character.Id, Hitboxes);
            boxes = Hitboxes.Count;
        }

        MarkSaved();
        return (written, boxes);
    }

    /// <summary>Back to what's on disk.</summary>
    public void Revert()
    {
        if (Source is null) return;
        Moves = new(Source.Moves);
        Own = [.. Source.Own];
        Hitboxes = new(savedHitboxes);
    }

    private void MarkSaved()
    {
        savedMoves = Moves.ToDictionary(m => m.Key, m => m.Value.ToSource());
        savedOwn = [.. Own];
        savedHitboxes = new(Hitboxes);
    }
}
