using System.Numerics;

using Fighter2D.Fighters;

using Horizon.Rendering;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.HitboxEditor;

// The canvas, the frame as big as it goes with its boxes over it, and dragging the red one about
internal sealed partial class EditorScene
{
    private static readonly Vector4 HurtColor = new(0.3f, 0.55f, 1.0f, 0.9f);
    private static readonly Vector4 TracedColor = new(1.0f, 0.82f, 0.2f, 0.9f);
    private static readonly Vector4 HitColor = new(1.0f, 0.18f, 0.16f, 1.0f);
    private static readonly Vector4 GhostColor = new(1.0f, 0.18f, 0.16f, 0.35f);
    private static readonly Vector4 GuideColor = new(1.0f, 1.0f, 1.0f, 0.08f);
    private static readonly Vector4 HandleColor = new(1.0f, 1.0f, 1.0f, 0.95f);

    // How close (in units of the canvas) the pointer has to be to a corner to take hold of it
    private const float HANDLE_REACH = 9.0f;
    private const float HANDLE_SIZE = 7.0f;

    private BoxView hurtView = null!, tracedView = null!, hitView = null!, frameView = null!;
    private Panel groundLine = null!, middleLine = null!;
    private readonly Panel[] handles = new Panel[4];

    // Steps of zoom the wheel has added to fitting the frame in, and how big a pixel of the art is in units of the canvas
    private int zoomSteps;
    private float zoom = 1.0f;

    // What the pointer is doing to the box. Moving it, a corner of it (0 to 3, see Corner), or drawing a new one
    private enum Drag { None, Move, Corner, Draw }
    private Drag drag;
    private int dragCorner;
    private Vector2 dragFrom;
    private Box dragStart;
    private bool pointerWasDown, dragChanged, scrubbing;

    private void BuildCanvas()
    {
        // Behind the frame, the edge of it, where the feet are and the middle the boxes are measured from
        frameView = new BoxView(canvas, 1.0f);
        groundLine = canvas.Add(new Panel { Anchor = Origin.Center, Color = GuideColor });
        middleLine = canvas.Add(new Panel { Anchor = Origin.Center, Color = GuideColor });

        // The frame goes under the boxes, so it is put first (the image was added first of all) and the boxes after
        hurtView = new BoxView(canvas, 1.0f);
        tracedView = new BoxView(canvas, 1.0f);
        hitView = new BoxView(canvas, 2.0f);

        for (int i = 0; i < handles.Length; i++)
            handles[i] = canvas.Add(new Panel { Anchor = Origin.Center, Size = new Vector2(HANDLE_SIZE), Color = HandleColor, Visible = false });
    }

    /// <summary>How big a pixel of the art is drawn, a whole number of units so every pixel is the same size.</summary>
    private float Zoom()
    {
        if (art is null || art.FrameSize.X <= 0) return 1.0f;

        Vector2 room = canvas.Size * 0.92f;
        float fit = MathF.Max(1.0f, MathF.Floor(MathF.Min(room.X / art.FrameSize.X, room.Y / art.FrameSize.Y)));
        return Math.Clamp(fit + zoomSteps, 1.0f, 24.0f);
    }

    /// <summary>A point of the frame (a share of it from its middle, Y up) in units of the canvas from its middle.</summary>
    private Vector2 ToCanvas(Vector2 share) => share * (art?.FrameSize ?? Vector2.One) * zoom;

    private Vector2 FromCanvas(Vector2 point) => point / ((art?.FrameSize ?? Vector2.One) * zoom);

    private void DrawFrame()
    {
        zoom = Zoom();
        if (art is null || timeline.Length == 0)
        {
            sprite.Visible = false;
            hurtView.Hide();
            tracedView.Hide();
            hitView.Hide();
            frameView.Hide();
            foreach (Panel handle in handles) handle.Visible = false;
            return;
        }

        TimelineFrame at = timeline[frame];
        Vector2 size = art.FrameSize * zoom;

        sprite.Visible = true;
        sprite.Atlas = art.Atlas;
        sprite.AtlasKey = KeyOf(at.Animation, at.Shown) ?? string.Empty;
        sprite.Size = size;

        frameView.Show(-size * 0.5f, size * 0.5f, GuideColor, 0.0f);
        groundLine.Position = new Vector2(0.0f, -size.Y * 0.5f);
        groundLine.Size = new Vector2(canvas.Size.X, 1.0f);
        middleLine.Position = Vector2.Zero;
        middleLine.Size = new Vector2(1.0f, size.Y);

        bool traced = art.Boxes.TryGet(at.Animation, (uint)at.Shown, out FrameBoxes boxes);
        if (traced && showHurtbox.State) hurtView.Show(ToCanvas(boxes.Hurtbox.Min), ToCanvas(boxes.Hurtbox.Max), HurtColor, 0.08f);
        else hurtView.Hide();

        // The traced hitbox is what the move hits with on its active frames when there is no red one, it's shown on them
        bool hits = at.Kind == FrameKind.Active;
        if (traced && showTraced.State && hits) tracedView.Show(ToCanvas(boxes.Hitbox.Min), ToCanvas(boxes.Hitbox.Max), TracedColor, Drawn is null ? 0.2f : 0.0f);
        else tracedView.Hide();

        // The box of this active frame, every one has its own. Off them, a ghost of the nearest one, with nothing to grab
        if (Drawn is { } drawn)
        {
            Box shown = drawn.At(ShownIndex);
            Vector2 min = ToCanvas(shown.Min), max = ToCanvas(shown.Max);
            hitView.Show(min, max, hits ? HitColor : GhostColor, hits ? 0.3f : 0.12f);
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i].Visible = hits;
                handles[i].Position = Corner(min, max, i);
            }
        }
        else
        {
            hitView.Hide();
            foreach (Panel handle in handles) handle.Visible = false;
        }
    }

    /// <summary>A corner of a box, 0 bottom left, 1 bottom right, 2 top right, 3 top left.</summary>
    private static Vector2 Corner(Vector2 min, Vector2 max, int corner) => corner switch
    {
        0 => min,
        1 => new Vector2(max.X, min.Y),
        2 => max,
        _ => new Vector2(min.X, max.Y)
    };

    /// <summary>Where the pointer is in the canvas, from its middle, Y up, and whether it's over it and nothing else is in the way.</summary>
    private (Vector2 Point, bool Over) CanvasPointer(UIPointer pointer)
    {
        Vector2 local = module.ToLocal(pointer.Position);
        UIRect area = canvas.Bounds;
        return (local - area.Center, area.Contains(local) && module.Popup is null);
    }

    private void UpdateCanvasPointer()
    {
        UIPointer pointer = ui.Pointer;
        bool pressed = pointer.Down && !pointerWasDown;
        bool released = !pointer.Down && pointerWasDown;
        pointerWasDown = pointer.Down;

        var (point, over) = CanvasPointer(pointer);

        // The wheel zooms, a whole pixel at a time
        float wheel = Engine.Input.Mouse.Scroll;
        if (over && MathF.Abs(wheel) > 0.01f)
        {
            zoomSteps += wheel > 0 ? 1 : -1;
            Redraw();
        }

        // A press on the timeline shows that frame, and dragging along it scrubs
        int cell = module.Popup is null ? CellUnder(module.ToLocal(pointer.Position)) : -1;
        if (pressed && cell >= 0) scrubbing = true;
        if (!pointer.Down) scrubbing = false;
        if (scrubbing && cell >= 0 && cell != frame) ShowFrame(cell);

        if (pressed && over && move is not null && art is not null && timeline.Length > 0) Grab(point);
        else if (drag != Drag.None && pointer.Down) DragTo(point);

        if (released && drag != Drag.None)
        {
            // A click on nothing that didn't draw anything isn't a change
            if (!dragChanged) undo.Pop();
            drag = Drag.None;
            AfterChange();
        }
    }

    /// <summary>Takes hold of a corner of the red box, the box itself, or the canvas to draw a new one on.</summary>
    private void Grab(Vector2 point)
    {
        // The hitbox is only out on the active frames, so that's the only place its boxes are
        if (ActiveIndex < 0)
        {
            Say(Drawn is null
                ? "a hitbox is drawn on the hit frame, the red cell marked HIT"
                : "the hitbox is only out on the active frames, the red cells, step onto one to change its box there");
            return;
        }

        dragFrom = point;
        dragChanged = false;
        PushUndo();

        if (Drawn is { } drawn)
        {
            dragStart = drawn.At(ActiveIndex);
            Vector2 min = ToCanvas(dragStart.Min), max = ToCanvas(dragStart.Max);
            for (int i = 0; i < 4; i++)
            {
                if (Vector2.Distance(point, Corner(min, max, i)) > HANDLE_REACH) continue;
                drag = Drag.Corner;
                dragCorner = i;
                return;
            }

            if (point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y)
            {
                drag = Drag.Move;
                return;
            }
        }

        drag = Drag.Draw;
        dragStart = new Box(FromCanvas(point), FromCanvas(point));
    }

    private void DragTo(Vector2 point)
    {
        if (move is null || art is null) return;

        // In whole pixels of the art
        Vector2 moved = Vector2.Round(FromCanvas(point - dragFrom) * art.FrameSize) / art.FrameSize;
        Box box = dragStart;

        switch (drag)
        {
            case Drag.Move:
                box = new Box(dragStart.Min + moved, dragStart.Max + moved);
                break;

            case Drag.Corner:
                Vector2 min = dragStart.Min, max = dragStart.Max;
                if (dragCorner is 0 or 3) min.X += moved.X; else max.X += moved.X;
                if (dragCorner is 0 or 1) min.Y += moved.Y; else max.Y += moved.Y;
                box = Ordered(min, max);
                break;

            case Drag.Draw:
                // Nothing until it has been dragged a pixel or two, a click on its own draws nothing
                if (Vector2.Distance(point, dragFrom) < 4.0f) return;
                box = Ordered(dragStart.Min, dragStart.Min + FromCanvas(point - dragFrom));
                break;
        }

        box = Snap(box);
        if (box.Max.X - box.Min.X <= 0.0f || box.Max.Y - box.Min.Y <= 0.0f) return;

        // Into the box of the active frame on screen, the others stay where they are
        string id = move.Id;
        int index = ActiveIndex;
        if (Edits.TryGetValue(id, out var was) && was.At(index) == box) return;

        Edits[id] = Drawn is { } drawn ? drawn.WithBox(index, box) : CustomHitbox.Of(box, 1);
        dragChanged = true;

        // Only what is on screen while it is dragged, the frame data and the list when it's let go
        DrawFrame();
        ToNumbers();
    }

    private static Box Ordered(Vector2 a, Vector2 b) => new(Vector2.Min(a, b), Vector2.Max(a, b));
}
