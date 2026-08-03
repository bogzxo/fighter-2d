using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Horizon.Engine;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.Text;
using Horizon.Rendering.UI;

using Silk.NET.Input;

namespace Fighter2D.Scenes;

internal class GamepadSelectorScene : Scene
{
    private GlyphRenderer glyphRenderer;
    public override Camera ActiveCamera { get; protected set; }

    public override void Initialize()
    {
        ActiveCamera = AddEntity(new Camera2D(new Vector2(1600, 900)));

        CompositeImages();
        CompositeText();

        base.Initialize();
    }

    private float _delayTimer = 0.0f;

    public override void UpdateState(float dt)
    {
        _delayTimer += dt;

        if (!Engine.InputManager.NativeInputContext!.Gamepads.Any())
        {
            glyphRenderer["insertGamepadHint"].IsVisible = true;
            glyphRenderer["gamepadPressX"].IsVisible = false;
        }
        else
        {
            glyphRenderer["insertGamepadHint"].IsVisible = false;
            glyphRenderer["gamepadPressX"].IsVisible = true;
        }

        base.UpdateState(dt);
        if (_delayTimer < 0.25f) return;

        foreach (var gamepad in Engine.InputManager.NativeInputContext!.Gamepads)
        {
            if (gamepad.A().Pressed)
            {
                Engine.SetScene(new MapSelectionScene(gamepad.Index));
            }
        }
    }

    private void CompositeImages()
    {
        var spriteBatch = AddEntity<SpriteBatch>();

        if (Engine.ObjectManager.Textures.TryCreateOrGet("gpselbg", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/backgrounds/player_select_bg.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_bg))
        {
            var bg = spriteBatch.AddEntity(new UIRectangle(Engine.WindowManager.WindowSize));
            bg.Transform.SetPositionRelativeToOrigin(new System.Numerics.Vector2(-Engine.WindowManager.WindowSize.X / 2, Engine.WindowManager.WindowSize.Y / 2));
            bg.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_bg.Asset, new Vector2(result_bg.Asset.Width, result_bg.Asset.Height)), "bg");

            spriteBatch.Add(bg);
        }
    }

    private void CompositeText()
    {
        glyphRenderer = AddEntity(new GlyphRenderer("Assets/Fonts/Born2bSporty", "Born2bSporty.fnt"));
        glyphRenderer.AddLabel("insertGamepadHint", new()
        {
            Text = "Insert Gamepad to Continue",
            Origin = Horizon.Rendering.Origin.Center
        });
        glyphRenderer.AddLabel("gamepadPressX", new()
        {
            Text = "Press X to Continue"
        });
    }
}