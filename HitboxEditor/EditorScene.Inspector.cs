using System.Numerics;

using Fighter2D.Moves;

using Horizon.HIDL;
using Horizon.HIDL.Runtime;
using Horizon.Rendering;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.HitboxEditor;

// The Move and Phases tabs of the inspector, every key a move and its phases can have in a move file. A change is read
// the way the game reads a move before it goes in, so a value the game wouldn't take is said out loud and left out
internal sealed partial class EditorScene
{
    // Typing into a box changes a move a letter at a time, one undo for the lot unless it stops for this long
    private const long UNDO_PAUSE_MS = 1500;

    private readonly List<FieldEditor> moveFields = [], phaseFields = [];
    private Label moveTitle = null!, moveWhere = null!, phaseWhere = null!;
    private Button takeOut = null!;
    private Dropdown phasePicker = null!;
    private TextBox phaseName = null!;
    private int phaseIndex;
    private bool showingMove;
    private (string Move, string What, long At) lastEdit;

    private void BuildMovePage(StackPanel page)
    {
        moveTitle = page.Add(new Label("") { Anchor = Origin.Left, TextScale = TEXT });
        moveWhere = page.Add(new Label("") { Anchor = Origin.Left, Size = new Vector2(INSPECTOR_WIDTH - 20, 0), TextScale = SMALL, Align = Origin.Left, Wrap = true, Color = Dim });
        takeOut = page.Add(new Button("Take it out of the own file") { Anchor = Origin.Left, Size = new Vector2(260, 28), LabelScale = TEXT, OnPressed = TakeOutOfOwnFile,
            Tooltip = "a move every character shares goes back to the shared one, a move of this character's own goes altogether" });

        foreach (FieldSpec spec in MoveFields.Move)
        {
            FieldEditor editor = FieldEditor.For(page, spec);
            editor.Changed = (changed, value) => Edit(draft => draft.With(changed.Key, value), changed.Key);
            moveFields.Add(editor);
        }
    }

    private void BuildPhasePage(StackPanel page)
    {
        var picking = page.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 4 });
        phasePicker = picking.Add(new Dropdown { Size = new Vector2(150, 28), TextScale = TEXT });
        phasePicker.OnChanged = _ =>
        {
            if (showingMove) return;
            phaseIndex = phasePicker.Index;
            ShowPhase();
            JumpToPhase();
        };

        picking.Add(new Button("+") { Size = new Vector2(30, 28), LabelScale = TEXT, OnPressed = AddPhase, Tooltip = "a new phase after this one" });
        picking.Add(new Button("-") { Size = new Vector2(30, 28), LabelScale = TEXT, OnPressed = RemovePhase, Tooltip = "this phase out" });
        picking.Add(new Button("up") { Size = new Vector2(44, 28), LabelScale = SMALL, OnPressed = () => MovePhase(-1), Tooltip = "this phase earlier" });
        picking.Add(new Button("down") { Size = new Vector2(56, 28), LabelScale = SMALL, OnPressed = () => MovePhase(1), Tooltip = "this phase later" });

        var naming = page.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 6 });
        naming.Add(new Label("name") { Size = new Vector2(112, 24), TextScale = 0.12f, Align = Origin.Left });
        phaseName = naming.Add(new TextBox { Size = new Vector2(196, 24), TextScale = 0.12f, MaxLength = 40, AllowedCharacters = "abcdefghijklmnopqrstuvwxyz0123456789_" });
        phaseName.OnChanged = RenamePhase;

        phaseWhere = page.Add(new Label("") { Anchor = Origin.Left, Size = new Vector2(INSPECTOR_WIDTH - 20, 0), TextScale = SMALL, Align = Origin.Left, Wrap = true, Color = Dim });

        foreach (FieldSpec spec in MoveFields.Phase)
        {
            FieldEditor editor = FieldEditor.For(page, spec);
            editor.Changed = (changed, value) => EditPhase(changed.Key, value);
            phaseFields.Add(editor);
        }
    }

    /// <summary>The animations and the moves of the character, for the fields that pick one.</summary>
    private void SetFieldOptions()
    {
        string[] animations = art is null ? [] : [.. art.Sprites.Sprites.Order()];
        string[] ids = [.. Here.Moves.Keys.Order()];

        foreach (FieldEditor editor in moveFields.Concat(phaseFields))
        {
            if (editor.Spec.Kind == FieldKind.Animation) editor.SetOptions(animations);
            else if (editor.Spec.Kind == FieldKind.Reroutes) editor.SetOptions(ids);
        }
    }

    /// <summary>Puts what the move says into the Move and Phases tabs.</summary>
    private void ShowMove()
    {
        showingMove = true;
        MoveDraft? draft = Draft;

        if (draft is null || move is null)
        {
            moveTitle.Text = "no move";
            moveWhere.Text = string.Empty;
            takeOut.Enabled = false;
            phasePicker.Options = [];
            showingMove = false;
            return;
        }

        string own = Here.Source?.OwnFile ?? "the own file";
        bool shared = Here.Source?.Shared.ContainsKey(move.Id) ?? false;
        moveTitle.Text = move.Id + (Here.Changed(move.Id) ? "   (changed)" : "");
        moveWhere.Text = Here.Own.Contains(move.Id)
            ? shared ? $"In {own}, over the one every character shares." : $"In {own}, this character's alone."
            : $"Shared with the other characters. Changing it puts a copy of it in {own}, for this character alone.";
        takeOut.Enabled = Here.Own.Contains(move.Id);

        foreach (FieldEditor editor in moveFields) editor.Show(draft.Fields.Get(editor.Spec.Key));

        phasePicker.Options = [.. draft.Phases.Select(p => p.Name)];
        phaseIndex = Math.Clamp(phaseIndex, 0, Math.Max(0, draft.Phases.Length - 1));
        phasePicker.Index = phaseIndex;
        showingMove = false;
        ShowPhase();
    }

    private void ShowPhase()
    {
        if (Draft is not { } draft) return;

        bool any = draft.Phases.Length > 0;
        foreach (FieldEditor editor in phaseFields)
        {
            editor.Root.Visible = any;
            if (any) editor.Show(draft.Phases[phaseIndex].Fields.Get(editor.Spec.Key));
        }

        if (!phaseName.IsFocused) phaseName.Text = any ? draft.Phases[phaseIndex].Name : string.Empty;

        int first = Array.FindIndex(timeline, f => f.Phase == phaseIndex), last = Array.FindLastIndex(timeline, f => f.Phase == phaseIndex);
        phaseWhere.Text = !any ? "No phases, the move does nothing. Add one with +."
            : first < 0 ? "Not on the timeline (it's past a phase that loops for ever, or 0 frames long)."
            : $"Phase {phaseIndex + 1} of {draft.Phases.Length}, frames {first + 1} to {last + 1} of the timeline.";
    }

    /// <summary>The phase the frame on screen is in, picked in the Phases tab, as the timeline is stepped through.</summary>
    private void SelectPhase(int index)
    {
        if (index == phaseIndex || Draft is not { } draft || index < 0 || index >= draft.Phases.Length) return;

        phaseIndex = index;
        showingMove = true;
        phasePicker.Index = index;
        showingMove = false;
        ShowPhase();
    }

    /// <summary>The frame on screen onto the first of the phase that was picked.</summary>
    private void JumpToPhase()
    {
        int first = Array.FindIndex(timeline, f => f.Phase == phaseIndex);
        if (first >= 0 && timeline[frame].Phase != phaseIndex) ShowFrame(first);
    }

    /* Changing it */

    private void EditPhase(string key, IRuntimeValue? value)
    {
        int index = phaseIndex;
        Edit(draft => index < draft.Phases.Length ? draft.WithPhase(index, draft.Phases[index] with { Fields = draft.Phases[index].Fields.With(key, value) }) : draft,
            $"phase {index} {key}");
    }

    /// <summary>
    /// Helper method to change the move that's open. The change is read the way the game reads a move first, and if the
    /// game wouldn't take it it's said why and nothing changes (what's being typed stays in its box, to be finished).
    /// A move every character shares that is changed goes into the character's own move file.
    /// </summary>
    private bool Edit(Func<MoveDraft, MoveDraft> change, string what)
    {
        if (move is null || Draft is not { } draft) return false;

        string id = move.Id;
        MoveDraft changed = change(draft);
        try
        {
            MoveFileReader.ReadOne(id, changed.ToDictionary());
        }
        catch (Exception e)
        {
            Say($"{id} wouldn't read like that, {e.Message}");
            return false;
        }

        long now = System.Environment.TickCount64;
        if (lastEdit.Move != id || lastEdit.What != what || now - lastEdit.At > UNDO_PAUSE_MS) PushUndo();
        lastEdit = (id, what, now);

        Here.Moves[id] = changed;
        Here.Own.Add(id);
        AfterChange();
        return true;
    }

    private void AddPhase()
    {
        if (Draft is not { } draft) return;

        // A name nobody has, phase_2, phase_3 and on
        int n = draft.Phases.Length + 1;
        while (draft.Phases.Any(p => p.Name == $"phase_{n}")) n++;

        int at = draft.Phases.Length == 0 ? 0 : phaseIndex + 1;
        PhaseDraft[] phases = [.. draft.Phases[..at], new PhaseDraft($"phase_{n}", Fields.Empty), .. draft.Phases[at..]];
        lastEdit = default;
        if (Edit(d => d.WithPhases(phases), "phases"))
        {
            SelectPhaseAfterEdit(at);
            Say($"a new phase, phase_{n}, it carries on with the animation before it for a frame until it's told otherwise");
        }
    }

    private void RemovePhase()
    {
        if (Draft is not { } draft || draft.Phases.Length == 0) return;

        int at = phaseIndex;
        string name = draft.Phases[at].Name;
        lastEdit = default;
        if (Edit(d => d.WithPhases([.. d.Phases.Where((_, i) => i != at)]), "phases"))
        {
            SelectPhaseAfterEdit(Math.Max(0, at - 1));
            Say($"{name} is out");
        }
    }

    private void MovePhase(int by)
    {
        if (Draft is not { } draft) return;

        int from = phaseIndex, to = phaseIndex + by;
        if (to < 0 || to >= draft.Phases.Length) return;

        PhaseDraft[] phases = [.. draft.Phases];
        (phases[from], phases[to]) = (phases[to], phases[from]);
        lastEdit = default;
        if (Edit(d => d.WithPhases(phases), "phases")) SelectPhaseAfterEdit(to);
    }

    private void RenamePhase(string name)
    {
        if (showingMove || Draft is not { } draft || draft.Phases.Length == 0) return;

        name = name.Trim();
        int at = phaseIndex;
        if (name == draft.Phases[at].Name) return;
        if (!HIDLWriter.IsIdentifier(name))
        {
            Say($"'{name}' can't be the name of a phase, letters, digits and _ and not a digit first");
            return;
        }

        if (draft.Phases.Any(p => p.Name == name))
        {
            Say($"the move has a phase called {name} already");
            return;
        }

        if (Edit(d => d.WithPhase(at, d.Phases[at] with { Name = name }), $"phase {at} name"))
            phasePicker.Options = [.. Draft!.Phases.Select(p => p.Name)];
    }

    private void SelectPhaseAfterEdit(int index)
    {
        phaseIndex = -1;
        SelectPhase(index);
        JumpToPhase();
    }

    /// <summary>A new move, a copy of the one that's open (and its red box) under the name typed in, in the own file.</summary>
    private void DuplicateMove()
    {
        if (move is null || Draft is not { } draft) return;

        string name = newMoveName.Text.Trim();
        if (!HIDLWriter.IsIdentifier(name))
        {
            Say("type the name of the new move in the box first, letters, digits and _");
            return;
        }

        if (Here.Moves.ContainsKey(name))
        {
            Say($"{character.PrettyName} has a move called {name} already");
            return;
        }

        PushUndo();
        Here.Moves[name] = draft with { Id = name };
        Here.Own.Add(name);
        if (Drawn is { } drawn) Edits[name] = drawn;

        newMoveName.Text = string.Empty;
        SetFieldOptions();
        AfterChange();
        ListMoves(name);
        Say($"{name} is a copy of {draft.Id}. Nothing starts it until it has an input of its own or a move reroutes to it");
    }

    /// <summary>
    /// The move out of the character's own move file. One every character shares goes back to the shared one, one of
    /// the character's own goes altogether, with its red box.
    /// </summary>
    private void TakeOutOfOwnFile()
    {
        if (move is null || !Here.Own.Contains(move.Id)) return;

        string id = move.Id;
        PushUndo();
        Here.Own.Remove(id);
        bool shared = Here.Source?.Shared.ContainsKey(id) ?? false;
        if (shared) Here.Moves[id] = Here.Source!.Shared[id];
        else
        {
            Here.Moves.Remove(id);
            Edits.Remove(id);
        }

        if (!Rebuild(out string? error))
        {
            // Something else reroutes to it, it has to stay
            Here.Restore(undo.Pop().State);
            Rebuild(out _);
            Say($"{id} can't go, {error}");
            return;
        }

        lastEdit = default;
        SetFieldOptions();
        AfterChange();
        if (!shared) ListMoves(null);
        Say(shared ? $"{id} is the shared one again" : $"{id} is gone");
    }
}
