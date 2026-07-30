using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Horizon.Engine;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.Text;
using Horizon.Rendering.UI;

using ImGuiNET;

namespace CumInstinctDuel.Scenes;

internal class MainMenuScene : Scene
{
    public override Camera ActiveCamera { get; protected set; } = null!;
    private Camera2D camera = null!;

   
    // UI Elements
    private SpriteBatch spriteBatch = null!;
    private UIRectangle logo =null!, bg = null!;
    private GlyphRenderer glyphRenderer = null!;

    public override void Initialize()
    {
        spriteBatch = AddEntity<SpriteBatch>();

        Engine.GL.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        Engine.GL.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);

        if (Engine.ObjectManager.Textures.TryCreateOrGet("main_logo", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/ui/logo.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_logo))
        {
            const uint logoScalar = 2;

            logo = spriteBatch.AddEntity(new UIRectangle(new System.Numerics.Vector2(result_logo.Asset.Width * logoScalar, result_logo.Asset.Height * logoScalar)));
            logo.Transform.SetPositionRelativeToOrigin(new System.Numerics.Vector2(-Engine.WindowManager.WindowSize.X / 2, Engine.WindowManager.WindowSize.Y/2));
            logo.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_logo.Asset, new System.Numerics.Vector2(128,48)), "logo");

            spriteBatch.Add(logo);
        }
        if (Engine.ObjectManager.Textures.TryCreateOrGet("main_bg", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/backgrounds/menu.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_bg))
        {
            bg = spriteBatch.AddEntity(new UIRectangle(Engine.WindowManager.WindowSize));
            bg.Transform.SetPositionRelativeToOrigin(new System.Numerics.Vector2(-Engine.WindowManager.WindowSize.X / 2, Engine.WindowManager.WindowSize.Y / 2));
            bg.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_bg.Asset, new Vector2(result_bg.Asset.Width, result_bg.Asset.Height)), "bg");

            spriteBatch.Add(bg);
        }


        ActiveCamera = camera = AddEntity<Camera2D>(new(new System.Numerics.Vector2(Engine.WindowManager.WindowSize.X, Engine.WindowManager.WindowSize.Y)));

        glyphRenderer = AddEntity(new GlyphRenderer("Assets/Fonts/Born2bSporty", "Born2bSporty.fnt"));
        glyphRenderer.Initialize();

        glyphRenderer.AddLabel("hint", new TextLabel
        {
            Text = "Press X to begin!",
            Origin = Origin.Center,
            Transform = {
                Size = Vector2.One * 0.5f
            }
        });

        glyphRenderer.AddLabel("version", new TextLabel
        {
            Text = "v.preCUM",
            Origin = Origin.TopRight,
            Transform = {
                Position = new Vector2(Engine.WindowManager.WindowSize.X / 2, -Engine.WindowManager.WindowSize.Y / 2)
            }
        });

        base.Initialize();
    }

    private float time;
    public override void UpdatePhysics(float dt)
    {
        time += dt;
        base.UpdatePhysics(dt);

        if (Engine.InputManager.IsPressed(Horizon.Input.VirtualAction.Interact))
        {
            Engine.SetScene(new MapSelectionScene());
        }
    }
    protected override void DisposeOther()
    {
        base.DisposeOther();

        Engine.ObjectManager.Textures.Remove("main_logo");
    }
}
