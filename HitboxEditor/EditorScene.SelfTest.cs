using System.Numerics;

using Fighter2D.Content;
using Fighter2D.Fighters;
using Fighter2D.Moves;

using Horizon.HIDL.Runtime;

using Horizon.Logging;
using Horizon.UI;

namespace Fighter2D.HitboxEditor;

// The editor clicking through itself with a pretend pointer, drawing a box, moving it, sizing it, scrubbing the
// timeline, undoing and saving, and saying whether every bit of that did what it should. The hitbox file of the
// character it tried it on is put back the way it was at the end, whatever happened
internal sealed partial class EditorScene
{
    private IEnumerator<int>? selfTest;
    private int selfTestWait;
    private UIPointer fake;
    private int failures;
    private string? fileBefore, ownBefore;
    private bool hadFile;

    private void StepSelfTest(float dt)
    {
        if (selfTest is null)
        {
            ui.PointerSource = () => fake;
            selfTest = SelfTest();
        }

        if (selfTestWait-- > 0) return;

        try
        {
            if (selfTest.MoveNext()) selfTestWait = selfTest.Current;
            else FinishSelfTest();
        }
        catch (Exception e)
        {
            Check(false, $"the self test threw, {e}");
            FinishSelfTest();
        }
    }

    private IEnumerator<int> SelfTest()
    {
        string path = GameContent.PathOf(CustomHitboxes.FileOf(character.Id));
        hadFile = File.Exists(path);
        fileBefore = hadFile ? File.ReadAllText(path) : null;

        // Somewhere quiet to point while things settle
        fake = new UIPointer(module.ToWorld(Vector2.Zero), false);
        yield return 20;

        int hitting = Array.FindIndex(moveIds, id => moves.Moves[id].Phases.Any(p => p.HitFrame >= 0));
        Check(hitting >= 0, $"{character.Id} has a move that hits");
        if (hitting < 0) yield break;

        OpenMove(hitting);
        if (Drawn is not null) DeleteBox();
        yield return 2;
        Check(Drawn is null, "starts with no red box");
        Check(timeline.Length > 0 && timeline[frame].IsHitFrame, "a move opens on its hit frame");

        // Drawn on nothing, from a little left of the middle up and to the right
        Vector2 middle = canvas.Bounds.Center;
        foreach (int wait in Press(middle + new Vector2(-10, 0), middle + new Vector2(50, 40))) yield return wait;
        Check(Drawn is not null, "dragging on nothing draws a box");
        if (Drawn is not { } drawn) yield break;
        Box first = drawn.At(0);
        Check(first.Max.X > first.Min.X && first.Max.Y > first.Min.Y, "the box drawn is the right way round");
        Check(Describe(move!.Id).Contains("(drawn)"), "the list says the move has a box");

        // Moved by its middle, two pixels of art to the right
        Vector2 inside = middle + ToCanvas((first.Min + first.Max) * 0.5f);
        foreach (int wait in Press(inside, inside + new Vector2(2 * zoom, 0))) yield return wait;
        Box moved = Drawn!.Value.At(0);
        Check(Near(moved.Min.X - first.Min.X, 2.0f / art!.FrameSize.X), $"dragging the box moves it two pixels ({(moved.Min.X - first.Min.X) * art.FrameSize.X})");
        Check(Near(moved.Max.Y - moved.Min.Y, first.Max.Y - first.Min.Y), "moving the box keeps its size");

        // Its top right corner out by three pixels each way
        Vector2 corner = middle + ToCanvas(moved.Max);
        foreach (int wait in Press(corner, corner + new Vector2(3 * zoom, 3 * zoom))) yield return wait;
        Box sized = Drawn!.Value.At(0);
        Check(Near(sized.Max.X - moved.Max.X, 3.0f / art.FrameSize.X) && Near(sized.Min.X, moved.Min.X),
            "dragging a corner sizes the box from that corner");

        // The numbers say the same as the box
        Check(Near(boxRight.Value, MathF.Round(sized.Max.X * art.FrameSize.X), 0.01f), "the front edge number follows the box");

        // More active frames, which the timeline shows
        // As many as there are frames left from the hit frame on, a move that ends sooner cuts them short
        activeBox.Value = 3;
        FromNumbers();
        yield return 2;
        Check(Drawn!.Value.Active == 3 && Drawn.Value.IsSameThroughout, "the active frames number sets the box's, every new frame with the same box");
        int fits = Math.Min(3, timeline.Length - MoveTimeline.HitFrame(timeline));
        Check(timeline.Count(f => f.Kind == FrameKind.Active) == fits, $"the timeline has as many active frames as the box says, or as fit ({fits})");

        // A box of its own on the second active frame, the hit frame's left where it was
        bool perFrame = fits >= 2;
        if (perFrame)
        {
            int hitAt = MoveTimeline.HitFrame(timeline);
            ShowFrame(hitAt + 1);
            yield return 2;
            Box second = Drawn!.Value.At(1);
            Vector2 grab = middle + ToCanvas((second.Min + second.Max) * 0.5f);
            foreach (int wait in Press(grab, grab + new Vector2(4 * zoom, 0))) yield return wait;
            Box secondMoved = Drawn!.Value.At(1);
            Check(Near(secondMoved.Min.X - second.Min.X, 4.0f / art.FrameSize.X) && Drawn.Value.At(0) == sized && Drawn.Value.At(2) == sized,
                "moving the box on the second active frame moves that frame's box and no other");
            Check(Near(boxLeft.Value, MathF.Round(secondMoved.Min.X * art.FrameSize.X), 0.01f), "the numbers on the second active frame are its box");

            ShowFrame(hitAt);
            yield return 2;
            Check(Near(boxLeft.Value, MathF.Round(sized.Min.X * art.FrameSize.X), 0.01f), "back on the hit frame the numbers are the hit frame's box");

            if (hitAt > 0)
            {
                ShowFrame(0);
                yield return 2;
                CustomHitbox before = Drawn!.Value;
                foreach (int wait in Press(grab, grab + new Vector2(6 * zoom, 0))) yield return wait;
                Check(Drawn!.Value == before, "a startup frame has no box to drag, nothing changes");
            }

            Save();
            yield return 2;
            var saved = CustomHitboxes.Load(character.Id);
            Check(saved.TryGetValue(move!.Id, out var back) && back.Active == 3 && !back.IsSameThroughout && Near(back.At(1).Min.X, secondMoved.Min.X) && Near(back.At(0).Min.X, sized.Min.X),
                "a box a frame is saved and read back as a box a frame");

            Undo();
            yield return 2;
            Check(Drawn!.Value.IsSameThroughout, "undo puts the second frame's box back");
        }

        // The first cell of the timeline
        Vector2 firstCell = cells[0].Bounds.Center;
        foreach (int wait in Press(firstCell, firstCell)) yield return wait;
        Check(frame == 0, "clicking a cell of the timeline shows that frame");

        Undo();
        yield return 2;
        Check(Drawn!.Value.Active == 1 && Drawn.Value.At(0) == sized, "undo takes the active frames back off");

        Save();
        yield return 2;
        var onDisk = CustomHitboxes.Load(character.Id);
        Check(onDisk.TryGetValue(move!.Id, out var read) && read.Active == 1 && Near(read.At(0).Min.X, sized.Min.X) && Near(read.At(0).Max.Y, sized.Max.Y),
            "saving writes the box and it reads back the same");
        Check(!Unsaved, "nothing is unsaved after a save");

        DeleteBox();
        yield return 2;
        Check(Drawn is null && Unsaved, "delete takes the box away and it's unsaved");

        Revert();
        yield return 2;
        Check(Drawn is not null, "revert brings back what's on disk");

        foreach (int wait in MoveChecks()) yield return wait;
    }

    /// <summary>Changing a move and its phases, copying one, saving it into the own file and taking it out again.</summary>
    private IEnumerable<int> MoveChecks()
    {
        string own = GameContent.PathOf(Here.Source!.OwnFile);
        ownBefore ??= File.ReadAllText(own);
        string id = move!.Id;
        inspector.Select(1);
        yield return 3;

        FieldEditor damage = moveFields.First(f => f.Spec.Key == "damage");
        damage.Changed!(damage.Spec, new NumberValue(33));
        yield return 2;
        Check(move!.Damage == 33, "the damage field sets the move's damage");
        Check(moveList.Items[moveList.Index].StartsWith("* "), "the list stars a changed move");
        Check(Here.Own.Contains(id), "a changed move goes in the own file");

        FieldEditor level = moveFields.First(f => f.Spec.Key == "level");
        level.Changed!(level.Spec, new StringValue("sideways"));
        yield return 2;
        Check(Draft!.Fields.Get("level") is not StringValue { Value: "sideways" } && status.Text.Contains("sideways"), "a value the game wouldn't take is turned down and said why");

        // The hit a frame later, the hit frame on the timeline with it
        int hitPhase = Draft!.Phases.ToList().FindIndex(p => p.Fields.Get("hit") is NumberValue);
        if (hitPhase >= 0)
        {
            inspector.Select(2);
            int before = MoveTimeline.HitFrame(timeline);
            SelectPhaseAfterEdit(hitPhase);
            float hit = ((NumberValue)Draft!.Phases[hitPhase].Fields.Get("hit")!).Value;
            FieldEditor hitField = phaseFields.First(f => f.Spec.Key == "hit");
            hitField.Changed!(hitField.Spec, new NumberValue(hit + 1));
            yield return 2;
            int after = MoveTimeline.HitFrame(timeline);
            Check(after > before, $"moving the hit a frame moves the hit frame on the timeline ({before} to {after})");

            int phases = Draft!.Phases.Length;
            AddPhase();
            yield return 2;
            Check(Draft!.Phases.Length == phases + 1, "+ adds a phase");
            RenamePhase("selftest_phase");
            yield return 2;
            Check(Draft!.Phases.Any(p => p.Name == "selftest_phase"), "a phase can be renamed");
            RemovePhase();
            yield return 2;
            Check(Draft!.Phases.Length == phases && Draft.Phases.All(p => p.Name != "selftest_phase"), "- takes it out again");
        }

        Undo();
        yield return 2;
        Check(Draft!.Phases.Length > 0, "undo after phases works");
        Revert();
        yield return 2;
        Check(!Here.Changed(id), "revert puts the move back the way the file has it");

        // A copy under a new name, saved, then taken out and saved, and the file is the way it was to the byte
        newMoveName.Text = "selftest_copy";
        DuplicateMove();
        yield return 2;
        Check(move?.Id == "selftest_copy" && Here.Own.Contains("selftest_copy"), "copy as makes a new move in the own file");

        Save();
        yield return 2;
        string saved = File.ReadAllText(own);
        Check(saved.Contains("selftest_copy: {") && saved.StartsWith(ownBefore[..Math.Min(200, ownBefore.Length)]), "saving writes the new move and leaves the rest of the file be");
        Check(MoveSource.Load(character).Moves.ContainsKey("selftest_copy"), "the saved file reads back with the new move in it");

        TakeOutOfOwnFile();
        yield return 2;
        Check(!moves.Moves.ContainsKey("selftest_copy"), "taking a move of its own out of the own file deletes it");
        Save();
        yield return 2;
        Check(File.ReadAllText(own) == ownBefore, "putting a move in and taking it out leaves the own file as it was, byte for byte");

        // One of the own file's changed in place, and one every character shares changed into the own file
        foreach (bool inOwn in new[] { true, false })
        {
            string? pick = moveIds.FirstOrDefault(m => Here.Own.Contains(m) == inOwn && moves.Moves[m].Damage > 0);
            if (pick is null) continue;

            OpenMove(Array.IndexOf(moveIds, pick));
            yield return 2;
            damage.Changed!(damage.Spec, new NumberValue(41));
            Save();
            yield return 2;
            var read = MoveSource.Load(character);
            Check(read.Own.Contains(pick) && MoveFileReader.ReadOne(pick, read.Moves[pick].ToDictionary()).Damage == 41,
                inOwn ? $"a move of the own file ({pick}) is saved changed in place" : $"a shared move ({pick}) is saved into the own file, changed");
            Check(read.Moves.Count == Here.Moves.Count, "and the other moves are all still there");
        }

        // The fight it would start
        List<string> arguments = FightArguments();
        Check(arguments.Contains("--dummy") && arguments[arguments.IndexOf("--dummy") + 1] == "stand" && arguments.Contains(character.Id),
            $"Fight! starts the game as {character.Id} against a dummy that stands ({string.Join(' ', arguments)})");
        Check(File.Exists(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Fighter2D.exe" : "Fighter2D")), "the game is next to the editor to be started");
    }

    /// <summary>Presses the pointer at one point of the UI, drags it to another over a few ticks and lets go.</summary>
    private IEnumerable<int> Press(Vector2 from, Vector2 to)
    {
        fake = new UIPointer(module.ToWorld(from), false);
        yield return 2;
        fake = new UIPointer(module.ToWorld(from), true);
        yield return 2;

        for (int i = 1; i <= 6; i++)
        {
            fake = new UIPointer(module.ToWorld(Vector2.Lerp(from, to, i / 6.0f)), true);
            yield return 1;
        }

        fake = new UIPointer(module.ToWorld(to), false);
        yield return 3;
    }

    private static bool Near(float a, float b, float within = 0.0001f) => MathF.Abs(a - b) <= within;

    private void Check(bool passed, string what)
    {
        if (passed) Log.Info($"[Hitbox editor] pass {what}");
        else
        {
            failures++;
            Log.Error($"[Hitbox editor] FAIL {what}");
        }
    }

    private void FinishSelfTest()
    {
        string path = GameContent.PathOf(CustomHitboxes.FileOf(character.Id));
        if (hadFile) File.WriteAllText(path, fileBefore);
        else if (File.Exists(path)) File.Delete(path);
        if (ownBefore is not null && Here.Source is { } source) File.WriteAllText(GameContent.PathOf(source.OwnFile), ownBefore);

        Log.Info($"[Hitbox editor] self test done, {failures} failed");
        System.Environment.ExitCode = failures == 0 ? 0 : 1;
        Engine.WindowManager.Window.Close();
        selfTest = Enumerable.Empty<int>().GetEnumerator();
        selfTestWait = int.MaxValue;
    }
}
