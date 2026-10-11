using System.Numerics;

using Fighter2D.Fighters;
using Fighter2D.Moves;

using Horizon.Engine;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.UI;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.HitboxEditor;

/// <summary>
/// The editor, laid out like an animation editor. The moves of a character down the left, the frame in the middle as
/// big as it goes with its boxes over it, everything about the move on the right, and along the bottom the timeline of
/// the move, a cell a frame coloured by what the frame is (startup, active, recovery), the phases over it and the hit
/// frame marked under it.
/// <para>
/// The boxes. Blue is the hurtbox, where the frame can be hit, and yellow is the hitbox the game traces off the art (the
/// limb sticking out in front), both worked out by the game itself (SpriteBoxTracer) and not edited here. Red is the
/// hitbox drawn by hand for the move, which the game uses instead of the yellow one when there is one. It's out on the
/// move's active frames, from the hit frame for as many frames as it says, solid there and a ghost on every other
/// frame so it can be seen and moved about anywhere.
/// </para>
/// <para>
/// The Move and Phases tabs edit everything else a move is, every key its move file can have, and the timeline and
/// the frame data follow as it's typed. An edited move is saved into the character's own move file (see MoveSource),
/// the hitboxes into Assets/data/hitboxes. Fight! saves and starts the game on it against a dummy that stands there.
/// </para>
/// <para>
/// Drag inside the red box to move it, drag a corner to size it, drag on nothing to draw a new one. Delete takes it
/// away, Ctrl+Z puts back what was there before, Ctrl+S saves. Left and right step through the frames, space plays the
/// move, the wheel zooms.
/// </para>
/// </summary>
internal sealed partial class EditorScene : Scene
{
    private const string SKIN_DIRECTORY = "Assets/uix/flat/";
    private const string SKIN = "skin.hor";

    private const float TEXT = 0.13f;
    private const float SMALL = 0.11f;

    private const float INSPECTOR_WIDTH = 380.0f;
    private const float BODY_HEIGHT = 580.0f;

    private static readonly Vector4 Dim = new(0.58f, 0.6f, 0.66f, 1.0f);
    private static readonly Vector4 CanvasColor = new(0.05f, 0.055f, 0.07f, 1.0f);
    private static readonly Vector4 PanelColor = new(0.1f, 0.11f, 0.145f, 1.0f);

    public override Camera ActiveCamera { get; protected set; }

    private readonly EditorOptions options;
    private readonly UICompositor ui;
    private readonly UIModule module;

    private readonly Dropdown characterPicker;
    private readonly ListBox moveList;
    private readonly TextBox newMoveName;
    private readonly ToggleButton onlyHits, showHurtbox, showTraced;
    private readonly TabPanel inspector;
    private readonly Panel canvas;
    private readonly Image sprite;
    private readonly Label status, moveInfo, frameInfo, boxInfo;
    private readonly NumberBox boxLeft, boxBottom, boxRight, boxTop, activeBox;
    private readonly StackPanel phaseRow, cellRow, markRow;
    private readonly Button playButton;

    // What is being edited
    private readonly List<CharacterDefinition> characters;
    private CharacterDefinition character = null!;
    private CharacterArt? art;
    private MoveList moves = new();
    private string[] moveIds = [];
    private FightingMove? move;
    private TimelineFrame[] timeline = [];
    private int frame;

    // Everything about every character that has been opened, kept when another one is so nothing unsaved is lost
    private readonly Dictionary<string, Session> sessions = [];
    private readonly Stack<(string Character, Snapshot State)> undo = new();

    // The atlas keys of every animation that has been shown, by animation
    private readonly Dictionary<string, string[]> keys = [];

    private bool playing;
    private float playClock;

    // While the numbers of the inspector are set from the box, so setting them doesn't set the box back
    private bool syncing;

    private Session Here => sessions[character.Id];

    private Dictionary<string, CustomHitbox> Edits => Here.Hitboxes;

    private CustomHitbox? Drawn => move is not null && Edits.TryGetValue(move.Id, out var box) ? box : null;

    /// <summary>Which active frame the frame on screen is, 0 on the hit frame, -1 when it isn't one.</summary>
    private int ActiveIndex => timeline.Length > 0 && timeline[frame].Kind == FrameKind.Active ? frame - MoveTimeline.HitFrame(timeline) : -1;

    /// <summary>The active frame whose box the frame on screen shows, the nearest one when it isn't one itself.</summary>
    private int ShownIndex => ActiveIndex >= 0 ? ActiveIndex : frame < MoveTimeline.HitFrame(timeline) ? 0 : int.MaxValue;

    private MoveDraft? Draft => move is not null && Here.Moves.TryGetValue(move.Id, out var draft) ? draft : null;

    public EditorScene(EditorOptions options)
    {
        this.options = options;

        var camera = AddEntity(new Camera2D(Engine.WindowManager.ViewportSize));
        ActiveCamera = camera;
        ui = AddComponent(new UICompositor(camera, SKIN_DIRECTORY, SKIN));
        module = ui.CreateModule();

        var root = module.AddComponent(new StackPanel { Fill = UIFill.Both, Padding = new UIEdges(16, 12), Spacing = 10 });

        // Along the top, which character, what to do with it, and a fight on it
        var toolbar = root.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 10 });
        toolbar.Add(new Label("MOVES") { TextScale = 0.16f });
        characters = CharacterDefinition.LoadAll();
        characterPicker = toolbar.Add(new Dropdown([.. characters.Select(c => c.PrettyName)]) { Size = new Vector2(190, 30), TextScale = TEXT });
        toolbar.Add(new Button("Save") { Size = new Vector2(76, 30), LabelScale = TEXT, OnPressed = Save, Tooltip = "Ctrl+S, the moves into the character's own move file and the red boxes into its hitbox file" });
        toolbar.Add(new Button("Revert") { Size = new Vector2(76, 30), LabelScale = TEXT, OnPressed = Revert, Tooltip = "back to what's on disk" });
        toolbar.Add(new Button("Undo") { Size = new Vector2(76, 30), LabelScale = TEXT, OnPressed = Undo, Tooltip = "Ctrl+Z" });
        BuildFightControls(toolbar);
        status = toolbar.Add(new Label("") { Size = new Vector2(560, 30), TextScale = SMALL, Align = Origin.Left, Color = Dim, Wrap = true });

        var body = root.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 12 });

        // The moves
        var left = body.Add(new StackPanel { Anchor = Origin.Top, Spacing = 6 });
        left.Add(new Label("MOVES") { Anchor = Origin.Left, TextScale = TEXT, Color = Dim });
        moveList = left.Add(new ListBox { Size = new Vector2(250, 0), Rows = 20, TextScale = TEXT });
        onlyHits = left.Add(new ToggleButton("only moves that hit", startingState: true) { Anchor = Origin.Left, LabelScale = TEXT });
        var adding = left.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 6 });
        newMoveName = adding.Add(new TextBox { Size = new Vector2(150, 28), TextScale = TEXT, Placeholder = "new_move", MaxLength = 40,
            AllowedCharacters = "abcdefghijklmnopqrstuvwxyz0123456789_" });
        adding.Add(new Button("Copy as") { Size = new Vector2(94, 28), LabelScale = TEXT, OnPressed = DuplicateMove,
            Tooltip = "a new move, a copy of this one under the name typed in, in the character's own move file" });

        // The frame
        canvas = body.Add(new Panel { Size = new Vector2(860, BODY_HEIGHT), Color = CanvasColor, Radius = 6 });
        sprite = canvas.Add(new Image { Anchor = Origin.Center });
        BuildCanvas();

        // Everything about the move, a tab each for its red box, itself and its phases
        inspector = body.Add(new TabPanel { Anchor = Origin.Top, Tabs = ["Hitbox", "Move", "Phases"], TextScale = TEXT, Spacing = 8 });
        var hitbox = Page(inspector);
        moveInfo = hitbox.Add(new Label("") { Anchor = Origin.Left, Size = new Vector2(INSPECTOR_WIDTH - 20, 0), TextScale = SMALL, Align = Origin.Left, Wrap = true });
        hitbox.Add(new Label("RED HITBOX, in pixels of the frame from its middle") { Anchor = Origin.Left, TextScale = SMALL, Color = Dim });
        boxLeft = NumberRow(hitbox, "back edge");
        boxRight = NumberRow(hitbox, "front edge");
        boxBottom = NumberRow(hitbox, "bottom edge");
        boxTop = NumberRow(hitbox, "top edge");
        activeBox = NumberRow(hitbox, "active frames");
        boxInfo = hitbox.Add(new Label("") { Anchor = Origin.Left, Size = new Vector2(INSPECTOR_WIDTH - 20, 0), TextScale = SMALL, Align = Origin.Left, Wrap = true, Color = Dim });

        var buttons = hitbox.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 6 });
        buttons.Add(new Button("From the trace") { Size = new Vector2(160, 30), LabelScale = TEXT, OnPressed = FromTrace });
        buttons.Add(new Button("Delete") { Size = new Vector2(100, 30), LabelScale = TEXT, OnPressed = DeleteBox });
        hitbox.Add(new Button("This box on every active frame") { Anchor = Origin.Left, Size = new Vector2(266, 30), LabelScale = TEXT, OnPressed = SameThroughout });

        showHurtbox = hitbox.Add(new ToggleButton("show the hurtbox (blue)", startingState: true) { Anchor = Origin.Left, LabelScale = TEXT });
        showTraced = hitbox.Add(new ToggleButton("show the traced hitbox (yellow)", startingState: true) { Anchor = Origin.Left, LabelScale = TEXT });
        hitbox.Add(new Label("Every active frame has a box of its own. Drag the red box to move it, a corner to size it, nothing to draw one.\nDelete, Ctrl+Z, Ctrl+S. Left and right step, space plays, the wheel zooms.")
        {
            Anchor = Origin.Left, Size = new Vector2(INSPECTOR_WIDTH - 20, 0), TextScale = SMALL, Align = Origin.Left, Wrap = true, Color = Dim
        });

        BuildMovePage(Page(inspector));
        BuildPhasePage(Page(inspector));

        // The timeline of the move
        var bottom = root.Add(new StackPanel { Anchor = Origin.Left, Spacing = 3, Color = PanelColor, Radius = 6, Padding = new UIEdges(10, 8) });
        phaseRow = bottom.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = TIMELINE_GAP });
        cellRow = bottom.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = TIMELINE_GAP });
        markRow = bottom.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = TIMELINE_GAP });

        var controls = bottom.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 6 });
        controls.Add(new Button("|<") { Size = new Vector2(44, 28), LabelScale = TEXT, OnPressed = () => ShowFrame(0) });
        controls.Add(new Button("<") { Size = new Vector2(44, 28), LabelScale = TEXT, OnPressed = () => ShowFrame(frame - 1) });
        playButton = controls.Add(new Button("Play") { Size = new Vector2(80, 28), LabelScale = TEXT, OnPressed = TogglePlaying });
        controls.Add(new Button(">") { Size = new Vector2(44, 28), LabelScale = TEXT, OnPressed = () => ShowFrame(frame + 1) });
        controls.Add(new Button(">|") { Size = new Vector2(44, 28), LabelScale = TEXT, OnPressed = () => ShowFrame(timeline.Length - 1) });
        frameInfo = controls.Add(new Label("") { Size = new Vector2(900, 28), TextScale = TEXT, Align = Origin.Left });
        BuildLegend(bottom);

        // Started on whatever was asked for, the first character otherwise
        int start = Math.Max(0, characters.FindIndex(c => c.Id == options.Character));
        characterPicker.Index = start;
        OpenCharacter(characters[start]);

        characterPicker.OnChanged = _ => OpenCharacter(characters[characterPicker.Index]);
        moveList.OnChanged = _ => OpenMove(moveList.Index);
        onlyHits.OnPressed = () => ListMoves(move?.Id);
        showHurtbox.OnPressed = showTraced.OnPressed = Redraw;

        if (options.Move is { } wanted && !moveIds.Contains(wanted))
        {
            // Asked for by name, so it's shown even if it doesn't hit
            onlyHits.State = false;
            ListMoves(null);
        }

        if (options.Move is { } named && Array.IndexOf(moveIds, named) is var at and >= 0) OpenMove(at);
        if (options.Frame is { } startFrame) ShowFrame(startFrame);
        if (options.Tab is { } tab) inspector.Select(Math.Max(0, Array.FindIndex(inspector.Tabs, t => t.Equals(tab, StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>A page of the inspector, a window that scrolls with a stack in it.</summary>
    private static StackPanel Page(TabPanel tabs)
    {
        var scroll = tabs.Add(new ScrollPanel { Size = new Vector2(INSPECTOR_WIDTH, BODY_HEIGHT - 40), Padding = new UIEdges(4, 4) });
        return scroll.Add(new StackPanel { Anchor = Origin.Top, Spacing = 8, Size = new Vector2(INSPECTOR_WIDTH - 16, 0) });
    }

    private NumberBox NumberRow(StackPanel into, string name)
    {
        var row = into.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 10 });
        row.Add(new Label(name) { Size = new Vector2(140, 26), TextScale = TEXT, Align = Origin.Left });
        var box = row.Add(new NumberBox { Size = new Vector2(120, 26), DragStep = 1.0f });
        box.OnValueChanged = _ => FromNumbers();
        return box;
    }

    /* Characters and moves */

    private void OpenCharacter(CharacterDefinition opened)
    {
        character = opened;
        art = CharacterArt.Load(character);
        keys.Clear();

        // What is on disk, the first time the character is opened. After that whatever was left unsaved is kept
        if (!sessions.ContainsKey(character.Id))
        {
            try
            {
                sessions[character.Id] = Session.Load(character);
            }
            catch (Exception e)
            {
                sessions[character.Id] = Session.Broken(character);
                Say($"{character.PrettyName}'s moves couldn't be read, {e.Message}");
            }
        }

        SetFieldOptions();
        Rebuild(out _);
        ListMoves(null);
        Say($"{character.PrettyName}, {(art is null ? "no art" : $"{art.FrameSize.X} by {art.FrameSize.Y} frames")}, edited moves go in {Here.Source?.OwnFile ?? "nowhere"}");
    }

    /// <summary>
    /// Helper method to read every move of the character out of its drafts the way the game reads them, and work the
    /// frame data out again with the active frames the hand drawn hitboxes say, the way the game does as a fight starts.
    /// False and why when the moves don't make sense together (a reroute to a move that isn't there), the moves left as they were.
    /// </summary>
    private bool Rebuild(out string? error)
    {
        error = null;
        var read = new List<FightingMove>();
        foreach (var (id, draft) in Here.Moves)
        {
            try
            {
                read.Add(MoveFileReader.ReadOne(id, draft.ToDictionary()));
            }
            catch (Exception e)
            {
                error = $"{id} is wrong, {e.Message}";
                return false;
            }
        }

        try
        {
            moves = MoveList.Of(read);
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }

        Bake();
        if (move is not null) move = moves.Moves.GetValueOrDefault(move.Id);
        return true;
    }

    private void Bake()
    {
        foreach (FightingMove each in moves.Moves.Values)
        {
            bool drawn = Edits.TryGetValue(each.Id, out var box);
            each.CustomHitboxes = drawn ? box.Boxes : null;
            each.ActiveFrames = drawn ? box.Active : 1;
        }

        moves.Bake(animation => (uint)Math.Max(1, FrameCount(animation)), character.FrameRate);
    }

    private int FrameCount(string? animation) => art?.FrameCount(animation) ?? 1;

    private void ListMoves(string? keep)
    {
        moveIds = [.. moves.Moves.Values.Where(m => !onlyHits.State || m.Phases.Any(p => p.HitFrame >= 0) || m.Id == keep).Select(m => m.Id)];
        moveList.Items = [.. moveIds.Select(Describe)];

        int at = keep is null ? 0 : Math.Max(0, Array.IndexOf(moveIds, keep));
        if (moveIds.Length > 0) OpenMove(at);
        else
        {
            move = null;
            timeline = [];
            BuildTimeline();
            ShowMove();
            Redraw();
        }
    }

    /// <summary>A move in the list, with a star when it's changed since the last save and what it has drawn.</summary>
    private string Describe(string id)
    {
        string changed = Here.Changed(id) ? "* " : "";
        string drawn = Edits.ContainsKey(id) ? "   (drawn)" : "";
        return $"{changed}{id}{drawn}";
    }

    private void OpenMove(int index)
    {
        if (index < 0 || index >= moveIds.Length) return;

        if (moveList.Index != index) moveList.Index = index;
        move = moves.Moves[moveIds[index]];
        RebuildTimeline();
        ShowMove();

        // Straight onto the hit frame, that's what is being edited
        int hit = MoveTimeline.HitFrame(timeline);
        ShowFrame(hit >= 0 ? hit : 0);
    }

    private void RebuildTimeline()
    {
        timeline = move is null ? [] : MoveTimeline.Of(move, a => FrameCount(a), Drawn?.Active ?? 1);
        BuildTimeline();
    }

    private void ShowFrame(int index)
    {
        frame = timeline.Length == 0 ? 0 : Math.Clamp(index, 0, timeline.Length - 1);
        if (timeline.Length > 0) SelectPhase(timeline[frame].Phase);
        Redraw();
    }

    private void TogglePlaying()
    {
        playing = !playing;
        playClock = 0.0f;
        playButton.Label = playing ? "Pause" : "Play";
    }

    /* Changes, undo and saving */

    private void PushUndo() => undo.Push((character.Id, Here.Snapshot()));

    /// <summary>Puts a change to the hitboxes of the character in, with what was there before kept for undo.</summary>
    private void Change(Action<Dictionary<string, CustomHitbox>> change)
    {
        PushUndo();
        change(Edits);
        AfterChange();
    }

    /// <summary>Everything that hangs off the moves and the boxes put right after either changed.</summary>
    private void AfterChange()
    {
        if (!Rebuild(out string? error)) Say(error!);
        RebuildTimeline();
        moveList.Items = [.. moveIds.Select(Describe)];
        ShowMove();
        frame = Math.Clamp(frame, 0, Math.Max(0, timeline.Length - 1));
        Redraw();
    }

    /// <summary>The hitbox the game traces off the art on the hit frame, as the hand drawn one, to start from.</summary>
    private void FromTrace()
    {
        if (move is null || art is null) return;

        int hit = MoveTimeline.HitFrame(timeline);
        if (hit < 0)
        {
            Say($"{move.Id} doesn't hit anybody, there's no hit frame to take a box from");
            return;
        }

        // Every active frame from its own frame's trace, a frame with nothing traced on it the same as the one before
        int active = Drawn?.Active ?? 1;
        var boxes = new Box[active];
        int first = -1;
        for (int i = 0; i < active; i++)
        {
            TimelineFrame at = timeline[Math.Min(hit + i, timeline.Length - 1)];
            if (art.Boxes.TryGet(at.Animation, (uint)at.Shown, out FrameBoxes traced))
            {
                boxes[i] = Snap(traced.Hitbox);
                if (first < 0) first = i;
            }
            else if (i > 0) boxes[i] = boxes[i - 1];
        }

        if (first < 0)
        {
            Say($"the art of {move.Id} has nothing traced on its active frames");
            return;
        }

        // A frame before the first traced one takes that one
        for (int i = 0; i < first; i++) boxes[i] = boxes[first];

        string id = move.Id;
        Change(all => all[id] = new CustomHitbox(boxes));
        Say($"{id} has red hitboxes now, where the trace puts one on each of its {active} active frame{(active == 1 ? "" : "s")}");
    }

    /// <summary>The box of the active frame on screen on every active frame.</summary>
    private void SameThroughout()
    {
        if (move is null || Drawn is not { } drawn) return;

        string id = move.Id;
        Box box = drawn.At(ShownIndex);
        Change(all => all[id] = CustomHitbox.Of(box, drawn.Active));
        Say($"every active frame of {id} has this frame's box now");
    }

    private void DeleteBox()
    {
        if (move is null || Drawn is null) return;

        string id = move.Id;
        Change(all => all.Remove(id));
        Say($"{id} hits with what the trace finds again");
    }

    private void Undo()
    {
        if (undo.Count == 0)
        {
            Say("nothing to undo");
            return;
        }

        var (id, before) = undo.Pop();
        sessions[id].Restore(before);
        lastEdit = default;
        if (id != character.Id)
        {
            Say($"undid a change to {id}, which isn't the character that's open");
            return;
        }

        string? keep = move?.Id;
        AfterChange();
        if (keep is not null && !moves.Moves.ContainsKey(keep)) ListMoves(null);
        Say("undone");
    }

    private void Save()
    {
        try
        {
            var (moveCount, boxCount) = Here.Save();
            AfterChange();
            Say($"saved, {moveCount} move{(moveCount == 1 ? "" : "s")} to {Here.Source?.OwnFile}, {boxCount} hitbox{(boxCount == 1 ? "" : "es")} to {CustomHitboxes.FileOf(character.Id)}");
        }
        catch (Exception e)
        {
            Say($"couldn't save, nothing was written. {e.Message}");
        }
    }

    private void Revert()
    {
        PushUndo();
        Here.Revert();
        AfterChange();
        if (move is null || !moves.Moves.ContainsKey(move.Id)) ListMoves(null);
        Say("back to what's on disk");
    }

    private bool Unsaved => Here.Unsaved;

    /// <summary>The numbers of the inspector, as the box.</summary>
    private void FromNumbers()
    {
        if (syncing || move is null || art is null || Drawn is not { } drawn) return;

        Vector2 frameSize = art.FrameSize;
        float x0 = MathF.Min(boxLeft.Value, boxRight.Value - 1), x1 = MathF.Max(boxRight.Value, boxLeft.Value + 1);
        float y0 = MathF.Min(boxBottom.Value, boxTop.Value - 1), y1 = MathF.Max(boxTop.Value, boxBottom.Value + 1);
        var box = new Box(new Vector2(x0, y0) / frameSize, new Vector2(x1, y1) / frameSize);
        int active = Math.Max(1, (int)MathF.Round(activeBox.Value));

        // The edges are the box of the active frame on screen, the active frames the lot of them
        CustomHitbox changed = drawn.WithActive(active);
        if (ActiveIndex >= 0) changed = changed.WithBox(ActiveIndex, box);
        if (changed == drawn) return;

        string id = move.Id;
        Change(all => all[id] = changed);
    }

    /// <summary>The box, as the numbers of the inspector.</summary>
    private void ToNumbers()
    {
        syncing = true;
        if (Drawn is { } drawn && art is not null)
        {
            Vector2 frameSize = art.FrameSize;
            Box shown = drawn.At(ShownIndex);
            boxLeft.Value = MathF.Round(shown.Min.X * frameSize.X);
            boxRight.Value = MathF.Round(shown.Max.X * frameSize.X);
            boxBottom.Value = MathF.Round(shown.Min.Y * frameSize.Y);
            boxTop.Value = MathF.Round(shown.Max.Y * frameSize.Y);
            activeBox.Value = drawn.Active;
        }

        // The edges only on an active frame, they're that frame's box
        boxLeft.Enabled = boxRight.Enabled = boxBottom.Enabled = boxTop.Enabled = Drawn is not null && ActiveIndex >= 0;
        activeBox.Enabled = Drawn is not null;
        syncing = false;
    }

    /// <summary>A box onto the pixels of the art, a hitbox is so many pixels and not a fraction of one.</summary>
    private Box Snap(Box box)
    {
        if (art is null) return box;
        Vector2 size = art.FrameSize;
        return new Box(Vector2.Round(box.Min * size) / size, Vector2.Round(box.Max * size) / size);
    }

    private void Say(string text) => status.Text = text;

    /* Every update */

    public override void UpdateState(float dt)
    {
        base.UpdateState(dt);

        if (options.SelfTest) StepSelfTest(dt);

        UpdateKeys();
        UpdateCanvasPointer();

        if (playing && timeline.Length > 0)
        {
            playClock += dt;
            float frameTime = 1.0f / MathF.Max(1.0f, character.FrameRate);
            while (playClock >= frameTime)
            {
                playClock -= frameTime;
                ShowFrame((frame + 1) % timeline.Length);
            }
        }
    }

    private void UpdateKeys()
    {
        // Whatever is typed into a box is the box's
        if (ui.Focus is not null) return;

        var keys = Engine.Input.Keyboard;
        bool control = keys.Control;

        if (keys.WasPressed(Silk.NET.Input.Key.Right)) ShowFrame(frame + 1);
        if (keys.WasPressed(Silk.NET.Input.Key.Left)) ShowFrame(frame - 1);
        if (keys.WasPressed(Silk.NET.Input.Key.Home)) ShowFrame(0);
        if (keys.WasPressed(Silk.NET.Input.Key.End)) ShowFrame(timeline.Length - 1);
        if (keys.WasPressed(Silk.NET.Input.Key.Space)) TogglePlaying();
        if (keys.WasPressed(Silk.NET.Input.Key.Delete)) DeleteBox();
        if (control && keys.WasPressed(Silk.NET.Input.Key.Z)) Undo();
        if (control && keys.WasPressed(Silk.NET.Input.Key.S)) Save();
        if (keys.WasPressed(Silk.NET.Input.Key.Down) && moveIds.Length > 0) OpenMove(Math.Min(moveList.Index + 1, moveIds.Length - 1));
        if (keys.WasPressed(Silk.NET.Input.Key.Up) && moveIds.Length > 0) OpenMove(Math.Max(moveList.Index - 1, 0));
    }

    /// <summary>Everything on screen that depends on the move, the frame and the box, put right.</summary>
    private void Redraw()
    {
        DrawFrame();
        MarkTimeline();
        ToNumbers();

        if (move is null)
        {
            moveInfo.Text = "no move";
            frameInfo.Text = string.Empty;
            boxInfo.Text = string.Empty;
            return;
        }

        MoveFrameData data = move.FrameData;
        int hit = MoveTimeline.HitFrame(timeline);
        int active = Drawn?.Active ?? 1;
        moveInfo.Text = hit < 0
            ? $"{move.Id}, {timeline.Length} frames, doesn't hit anybody"
            : $"{move.Id}, {move.Damage} damage, {move.Level.ToString().ToLowerInvariant()}{(move.Knockdown ? ", knocks down" : "")}\n" +
              $"startup {data.Startup}f, active {active}, recovery {data.Recovery}f, hitstun {data.Hitstun}f\n" +
              $"on hit {Signed(data.OnHit)}, on block {Signed(data.OnBlock)} (60ths of a second)";

        boxInfo.Text = Drawn is null
            ? "No red box, the move hits with the yellow one the game traces off its art. Draw one on the hit frame, or take the trace's to start from."
            : ActiveIndex >= 0
                ? $"Active frame {ActiveIndex + 1} of {active}, this box is this frame's alone. Out from the hit frame for {active} frame{(active == 1 ? "" : "s")}, a box each."
                : $"Not an active frame, a ghost of the nearest one's box. Out from the hit frame for {active} frame{(active == 1 ? "" : "s")}, a box each.";

        if (timeline.Length == 0) return;
        TimelineFrame at = timeline[frame];
        frameInfo.Text = $"frame {frame + 1} of {timeline.Length}, {at.PhaseName}{(at.Loops ? " (loops)" : "")}, {at.Animation} {at.Shown + 1}, " +
                         $"{Kind(at.Kind)}{(at.IsHitFrame ? ", the hit frame" : "")}{(Unsaved ? "      unsaved changes" : "")}";

        static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();
    }

    private static string Kind(FrameKind kind) => kind switch
    {
        FrameKind.Startup => "startup",
        FrameKind.Active => "active, the hitbox is out",
        FrameKind.Recovery => "recovery",
        _ => "no hit"
    };

    /// <summary>The atlas key of a frame of an animation, asked for from the atlas the first time.</summary>
    private string? KeyOf(string animation, int shown)
    {
        if (art is null) return null;
        if (!keys.TryGetValue(animation, out string[]? found))
        {
            found = art.TryFind(animation, out SpriteSource source) ? art.Atlas.Request(source) : [];
            keys[animation] = found;
        }

        return found.Length == 0 ? null : found[Math.Clamp(shown, 0, found.Length - 1)];
    }
}
