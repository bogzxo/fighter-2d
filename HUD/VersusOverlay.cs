using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Egui.Viewport;

using Horizon.Core;
using Horizon.Engine;
using Horizon.Rendering.Spriting;

namespace Fighter2D.HUD;

internal class BarOverlay : Entity
{
    private readonly SpriteBatch viewportSb;
    private static SpriteSheet? _barSheet;
    private Sprite foreground, background;

    public Vector2 Position
    {
        set
        {
            foreground.Transform.Position = value;
            background.Transform.Position = value;
        }
    }

    public BarOverlay(SpriteBatch viewportSb)
    {
        this.viewportSb = viewportSb;

        foreground = AddEntity(new Sprite(new System.Numerics.Vector2(128, 8)));
        background = AddEntity(new Sprite(new System.Numerics.Vector2(128, 8)));
    }
    public override void Initialize()
    {
        if (_barSheet is null)
        {
            if (!foreground.LoadSpriteSheetFromDirectory("Assets/ui", "overlay.hor"))
                throw new Exception("fuck TODO fix");
            _barSheet = foreground.Spritesheet;
        }

        foreground.ConfigureSpriteSheet(_barSheet, "ui");

        viewportSb.Add(foreground);
        viewportSb.Add(background);

        base.Initialize();
    }
}

/* So we need to render a decent looking HUD with a VS counter,
 * a health bar, a rage bar, and a counter for the timedown
 */
internal class VersusOverlay : Entity
{
    public static readonly Vector2 ViewportSize = new(640, 360);
    private BarOverlay _barP1, _barP2;

    public VersusOverlay(SpriteBatch viewportSb)
    {
        _barP1 = AddEntity(new BarOverlay(viewportSb));
        _barP1.Position = new Vector2(-ViewportSize.X / 2, ViewportSize.Y / 2);

        _barP2 = AddEntity(new BarOverlay(viewportSb));
        _barP2.Position = new Vector2(-ViewportSize.X / 2, ViewportSize.Y / 2);
    }

}
