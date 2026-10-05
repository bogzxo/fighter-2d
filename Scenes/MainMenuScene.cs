using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Horizon.Engine;
using Horizon.Rendering;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Particles.Simulation;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.Text;

namespace Fighter2D.Scenes;

internal class MainMenuScene : Scene
{
    public override Camera ActiveCamera { get; protected set; } = null!;
    private Camera2D camera = null!;

    // UI Elements
    private SpriteBatch spriteBatch = null!;

    private Sprite logo = null!, bg = null!;
    private GlyphRenderer glyphRenderer = null!;
    private ParticleRenderer2D particlesSmoke, particlesFlame;

    public override void Initialize()
    {
        spriteBatch = AddEntity<SpriteBatch>();

        Engine.GL.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        Engine.GL.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);

        ActiveCamera = camera = AddEntity<Camera2D>(new(new System.Numerics.Vector2(Engine.WindowManager.WindowSize.X, Engine.WindowManager.WindowSize.Y)));

        if (Engine.ObjectManager.Textures.TryCreateOrGet("main_logo", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/ui/logo.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_logo))
        {
            const uint logoScalar = 3;

            logo = spriteBatch.AddEntity(new Sprite(new System.Numerics.Vector2(result_logo.Asset.Width * logoScalar, result_logo.Asset.Height * logoScalar)));
            logo.Transform.Origin = Origin.TopLeft;
            logo.Transform.Position = new Vector2(Engine.WindowManager.WindowSize.X/-2, Engine.WindowManager.WindowSize.Y/2);
            //logo.Transform.Position = new Vector2(0, 265);
            logo.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_logo.Asset, new System.Numerics.Vector2(result_logo.Asset.Width, result_logo.Asset.Height)), "logo");

            spriteBatch.Add(logo);
        }
        particlesSmoke = AddEntity(new ParticleRenderer2D(32768 * 2, new ComputeParticleSimulator2D()) {
            ParticleSize = 4
        });
        particlesFlame = AddEntity(new ParticleRenderer2D(32768 * 2, new ComputeParticleSimulator2D())
        {
            ParticleSize = 4
        });

        

        particlesSmoke.EndColor = new Vector3(0, 0, 0);

        particlesFlame.StartColor = new Vector3(255 / 255.0f, 64 / 255.0f, 0);
        particlesFlame.EndColor = new Vector3(54 / 255.0f, 31 / 255.0f, 0);

        glyphRenderer = AddEntity(new GlyphRenderer("Assets/Fonts/Born2bSporty", "Born2bSporty.fnt"));

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
            Text = Constants.VERSION_LABEL,
            Origin = Origin.TopRight,
            Transform = {
                Position = new Vector2(Engine.WindowManager.WindowSize.X / 2, -Engine.WindowManager.WindowSize.Y / 2)
            }
        });

        base.Initialize();
    }


    float timer = 0;
    public override void UpdatePhysics(float dt)
    {
        base.UpdatePhysics(dt);

        timer += dt;
        if (timer > 0.05)
        {
            timer = 0;

            for (int i = 0; i < 10; i++)
            {
                float x = -Engine.WindowManager.WindowSize.X / 2 + (Engine.WindowManager.WindowSize.X) * (i / 10.0f) + 32;
                

                particlesSmoke.AddCone(new Vector2(x, -Engine.WindowManager.WindowSize.Y + Random.Shared.NextSingle() * (Engine.WindowManager.WindowSize.Y / 2)), Vector2.UnitY + new Vector2((MathF.PI / 6.0f) * Random.Shared.NextSingle(), 0), MathF.PI / 6.0f, (int)(Random.Shared.NextSingle() * 100), 200 + 200 * Random.Shared.NextSingle());
                particlesFlame.AddCone(new Vector2(x, -Engine.WindowManager.WindowSize.Y * 0.6f - Random.Shared.NextSingle() * (Engine.WindowManager.WindowSize.Y / 2)), Vector2.UnitY + new Vector2((MathF.PI / 6.0f) * Random.Shared.NextSingle(), 0), MathF.PI / 12.0f, (int)(Random.Shared.NextSingle() * 100), 200 + 200 * Random.Shared.NextSingle());
            }
        }

        if (Engine.InputManager.IsPressed(Horizon.Input.VirtualAction.Interact))
        {
            Engine.SetScene(new GamepadSelectorScene());
        }
    }
}