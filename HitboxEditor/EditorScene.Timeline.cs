using System.Numerics;

using Horizon.HIDL.Runtime;
using Horizon.Rendering;
using Horizon.UI.Components;

namespace Fighter2D.HitboxEditor;

// The timeline along the bottom. A cell a frame of the move, coloured by what the frame is, the phases over the cells
// and the hit frame under them. Clicking a cell shows its frame, a right click on one offers to put the hit there
internal sealed partial class EditorScene
{
    private const float TIMELINE_GAP = 2.0f;
    private const float TIMELINE_WIDTH = 1500.0f;
    private const float CELL_HEIGHT = 34.0f;

    private static readonly Vector4 StartupColor = new(0.2f, 0.62f, 0.32f, 1.0f);
    private static readonly Vector4 ActiveColor = new(0.86f, 0.18f, 0.16f, 1.0f);
    private static readonly Vector4 RecoveryColor = new(0.22f, 0.36f, 0.78f, 1.0f);
    private static readonly Vector4 NeutralColor = new(0.32f, 0.34f, 0.4f, 1.0f);

    private readonly List<Panel> cells = [];
    private readonly List<Label> cellNumbers = [];

    private float CellWidth => timeline.Length == 0 ? 40.0f
        : Math.Clamp(MathF.Floor((TIMELINE_WIDTH - TIMELINE_GAP * (timeline.Length - 1)) / timeline.Length), 10.0f, 46.0f);

    private void BuildLegend(StackPanel into)
    {
        var legend = into.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 8 });
        Swatch(StartupColor, "startup, winding up, a hit now is a counter hit");
        Swatch(ActiveColor, "active, the hitbox is out from the hit frame");
        Swatch(RecoveryColor, "recovery, wide open, a hit now is a punish");
        Swatch(NeutralColor, "a move that doesn't hit");

        void Swatch(Vector4 color, string text)
        {
            legend.Add(new Panel { Size = new Vector2(14, 14), Color = color, Radius = 2 });
            legend.Add(new Label(text) { TextScale = SMALL, Color = Dim, Align = Origin.Left });
        }
    }

    /// <summary>Puts the cells of the timeline together for the move, as many as it has frames.</summary>
    private void BuildTimeline()
    {
        Clear(phaseRow);
        Clear(cellRow);
        Clear(markRow);
        cells.Clear();
        cellNumbers.Clear();

        float width = CellWidth;
        bool numbered = width >= 18.0f;

        // A label over every run of frames of the same phase, as wide as the run
        for (int i = 0; i < timeline.Length;)
        {
            int run = 1;
            while (i + run < timeline.Length && timeline[i + run].Phase == timeline[i].Phase) run++;

            string name = timeline[i].PhaseName + (timeline[i].Loops ? " (loops)" : "");
            phaseRow.Add(new Label(name) { Size = new Vector2(run * width + (run - 1) * TIMELINE_GAP, 18), TextScale = SMALL, Align = Origin.Left, Color = Dim });
            i += run;
        }

        foreach (TimelineFrame each in timeline)
        {
            var cell = cellRow.Add(new Panel { Size = new Vector2(width, CELL_HEIGHT), Color = ColorOf(each.Kind), Radius = 3 });
            var number = cell.Add(new Label(numbered ? (each.Index + 1).ToString() : "") { TextScale = SMALL });
            cells.Add(cell);
            cellNumbers.Add(number);

            int index = each.Index;
            cell.OnContextMenu = point => OfferHitFrame(index, point);

            // Under the hit frame, a mark saying so
            markRow.Add(new Label(each.IsHitFrame ? "HIT" : "") { Size = new Vector2(width, 16), TextScale = SMALL, Color = ActiveColor });
        }
    }

    private static void Clear(StackPanel panel)
    {
        foreach (var child in panel.Children.ToArray()) panel.Remove(child);
    }

    private static Vector4 ColorOf(FrameKind kind) => kind switch
    {
        FrameKind.Startup => StartupColor,
        FrameKind.Active => ActiveColor,
        FrameKind.Recovery => RecoveryColor,
        _ => NeutralColor
    };

    /// <summary>Shows which cell is the frame on screen, brighter than the rest and a little taller.</summary>
    private void MarkTimeline()
    {
        for (int i = 0; i < cells.Count && i < timeline.Length; i++)
        {
            bool here = i == frame;
            Vector4 color = ColorOf(timeline[i].Kind);
            cells[i].Color = here ? Vector4.Lerp(color, Vector4.One, 0.45f) with { W = 1.0f } : color;
            cells[i].Scale = new Vector2(1.0f, here ? 1.15f : 1.0f);
            cellNumbers[i].Color = here ? new Vector4(0.05f, 0.05f, 0.08f, 1.0f) : new Vector4(1, 1, 1, 0.85f);
        }
    }

    /// <summary>The menu a right click on a cell brings up. It steps onto the frame first, so what's on screen is what's meant.</summary>
    private void OfferHitFrame(int index, Vector2 point)
    {
        if (index < 0 || index >= timeline.Length) return;

        ShowFrame(index);

        var menu = new Menu("frame");
        menu.Add("Set as hit frame", () => SetHitFrame(index));
        ContextMenu.Show(module, point, menu);
    }

    /// <summary>
    /// The hit onto a frame of the timeline, which is the phase it's in and how far into it. The timeline has the one
    /// hit frame, so the hit comes out of whichever phase had it. The red boxes are counted from the hit frame and
    /// go along with it.
    /// </summary>
    private void SetHitFrame(int index)
    {
        if (Draft is not { } draft || index < 0 || index >= timeline.Length) return;

        if (timeline[index].IsHitFrame)
        {
            Say($"frame {index + 1} is the hit frame already");
            return;
        }

        int phase = timeline[index].Phase;
        if (phase >= draft.Phases.Length) return;

        int into = index - Array.FindIndex(timeline, f => f.Phase == phase);
        string name = draft.Phases[phase].Name;

        lastEdit = default;
        bool done = Edit(d =>
        {
            PhaseDraft[] phases = [.. d.Phases];
            for (int i = 0; i < phases.Length; i++)
                phases[i] = phases[i] with { Fields = phases[i].Fields.With("hit", i == phase ? new NumberValue(into) : null) };

            return d.WithPhases(phases);
        }, "hit frame");

        if (!done) return;

        ShowFrame(index);
        Say($"the hit is on frame {index + 1} now, frame {into + 1} of {name}");
    }

    /// <summary>Which cell of the timeline the pointer is over, -1 for none.</summary>
    private int CellUnder(Vector2 local)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].Bounds.Contains(local)) return i;
        }

        return -1;
    }
}
