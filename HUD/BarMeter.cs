using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Horizon.Core;
using Horizon.Engine;
using Horizon.Rendering.Spriting;

namespace Fighter2D.HUD;

internal class BarMeter : GameObject
{
    private static SpriteSheet _barSheet = null!;

    private Sprite foreground, background;

    public override void Initialize()
    {
        foreground = AddEntity(new Sprite(new System.Numerics.Vector2(128, 8)));
        background = AddEntity(new Sprite(new System.Numerics.Vector2(128 + 4, 8 + 4)));

        if (_barSheet == null)
        {
            if (Engine.ObjectManager.Textures.TryCreateOrGet("bars", new Horizon.OpenGL.Descriptions.TextureDescription
            {
                Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest,
                Paths = ["Assets/ui/bar.png"]
            }, out var result))
            {
                _barSheet = SpriteSheet.FromTexture(result.Asset, new System.Numerics.Vector2(128, 8));
                _barSheet.AddSprite("bg", new System.Numerics.Vector2(0, 0), new System.Numerics.Vector2(128, 8));
                _barSheet.AddSprite("fg", new System.Numerics.Vector2(0, 8), new System.Numerics.Vector2(128, 8));
            }
        }

        foreground.ConfigureSpriteSheet(_barSheet, "bg");

        base.Initialize();
    }

}
