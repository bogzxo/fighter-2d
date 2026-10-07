using System.Numerics;

namespace Fighter2D.Scenes;

/// <summary>
/// The text colours the menus share.
/// </summary>
internal static class MenuColors
{
    // Hints are dimmed by their colour rather than by being see-through, that would fade the icons in them as well
    public static readonly Vector4 Hint = new(0.58f, 0.6f, 0.66f, 1.0f);

    public static readonly Vector4 Name = new(0.0f, 0.86f, 1.0f, 1.0f);
    public static readonly Vector4 Ready = new(0.33f, 0.85f, 0.45f, 1.0f);
    public static readonly Vector4 Error = new(1.0f, 0.45f, 0.4f, 1.0f);
}
