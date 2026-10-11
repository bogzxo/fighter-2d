using System.Numerics;

using Horizon.Rendering;
using Horizon.UI.Components;

namespace Fighter2D.HitboxEditor;

/// <summary>
/// A box drawn over the frame in the canvas, four thin edges and a see through fill, out of plain panels of the UI.
/// Placed in units of the canvas from its middle, Y up, which is how its children are laid out anyway.
/// </summary>
internal sealed class BoxView
{
    private readonly Panel fill, top, bottom, left, right;
    private readonly float thickness;

    public BoxView(Panel canvas, float thickness)
    {
        this.thickness = thickness;
        fill = canvas.Add(Part());
        top = canvas.Add(Part());
        bottom = canvas.Add(Part());
        left = canvas.Add(Part());
        right = canvas.Add(Part());
        Hide();

        static Panel Part() => new() { Anchor = Origin.Center, Color = Vector4.One };
    }

    /// <summary>Shows the box from one corner to the other, the edges in a colour and the fill in the same at another opacity.</summary>
    public void Show(Vector2 min, Vector2 max, Vector4 edge, float fillOpacity)
    {
        Vector2 size = Vector2.Max(max - min, new Vector2(1.0f));
        Vector2 center = (min + max) * 0.5f;

        Place(fill, center, size, edge with { W = edge.W * fillOpacity }, fillOpacity > 0.0f);
        Place(top, new Vector2(center.X, max.Y - thickness * 0.5f), new Vector2(size.X, thickness), edge, true);
        Place(bottom, new Vector2(center.X, min.Y + thickness * 0.5f), new Vector2(size.X, thickness), edge, true);
        Place(left, new Vector2(min.X + thickness * 0.5f, center.Y), new Vector2(thickness, size.Y), edge, true);
        Place(right, new Vector2(max.X - thickness * 0.5f, center.Y), new Vector2(thickness, size.Y), edge, true);
    }

    public void Hide()
    {
        fill.Visible = top.Visible = bottom.Visible = left.Visible = right.Visible = false;
    }

    private static void Place(Panel part, Vector2 center, Vector2 size, Vector4 color, bool visible)
    {
        part.Visible = visible;
        part.Position = center;
        part.Size = size;
        part.Color = color;
    }
}
