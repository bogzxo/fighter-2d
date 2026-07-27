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

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
internal class MainMenuScene : Scene
{
    public override Camera ActiveCamera { get; protected set; }
    private Camera2D camera;

    // UI Config
    const uint VP_WIDTH = 640, VP_HEIGHT = 360;

    // UI Elements
    private SpriteBatch spriteBatch;
    private UIRectangle logo, button;
    private GlyphRenderer glyphRenderer;

    // State
    private bool inSelectionMenu;
    private MapLoader.MapDefinition[] mapDefinitions;
    private int selectedMapIndex = -1;

    public MainMenuScene() : base()
    {
        
    }

    public override void Initialize()
    {
        mapDefinitions = [.. MapLoader.LoadDefinitions("Assets/data/maps.hor")];

        spriteBatch = AddEntity<SpriteBatch>();


        Engine.GL.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        Engine.GL.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);

        if (Engine.ObjectManager.Textures.TryCreateOrGet("main_logo", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/ui/logo.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_logo))
        {
            const uint logoScalar = 2;

            logo = spriteBatch.AddEntity(new UIRectangle(new System.Numerics.Vector2(result_logo.Asset.Width * logoScalar, result_logo.Asset.Height * logoScalar)));
            logo.Transform.SetPositionRelativeToOrigin(new System.Numerics.Vector2(-VP_WIDTH / 2, VP_HEIGHT/2));
            logo.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_logo.Asset, new System.Numerics.Vector2(128,48)), "logo");

            spriteBatch.Add(logo);
        }

        if (Engine.ObjectManager.Textures.TryCreateOrGet("edge_button", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/ui/edge_button.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_button))
        {
            const uint buttonScalar = 2;

            button = spriteBatch.AddEntity(new UIRectangle(new System.Numerics.Vector2(result_button.Asset.Width * buttonScalar, result_button.Asset.Height * buttonScalar)));
            button.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_button.Asset, new System.Numerics.Vector2(64, 24)), "button");

            spriteBatch.Add(button);
        }

        Engine.GL.ClearColor(0.2f, 0.2f, 0.2f, 1.0f);

        ActiveCamera = camera = AddEntity<Camera2D>(new(new System.Numerics.Vector2(VP_WIDTH, VP_HEIGHT)));

        glyphRenderer = AddEntity<GlyphRenderer>();
        glyphRenderer.Initialize();

        glyphRenderer.AddLabel("test", new TextLabel
        {
            Text = "test",
            Transform = {
                Position = new Vector2(-VP_WIDTH / 2, -VP_HEIGHT / 2)
            }
        });

        glyphRenderer.AddLabel("ok", new TextLabel
        {
            Text = "ok",
            Transform = {
                Position = new Vector2(VP_WIDTH / 2 - glyphRenderer.CalculateSize("ok").X, -VP_HEIGHT / 2)
            }
        });

        base.Initialize();
    }

    private float time;
    public override void UpdatePhysics(float dt)
    {
        time += dt;
        base.UpdatePhysics(dt);

        //glyphRenderer.Transform.Rotation = MathF.Sin(time) * 90;

        if (Engine.InputManager.IsPressed(Horizon.Input.VirtualAction.Interact))
        {
            inSelectionMenu = true;
        }

        if (selectedMapIndex > -1)
        {
            Engine.SetScene(new Program(mapDefinitions[selectedMapIndex]));
        }
    }

    public override void UpdateState(float dt)
    {
        base.UpdateState(dt);
    }

    public override void Render(float dt, object? obj = null)
    {
        if (inSelectionMenu)
        {
            if (ImGui.Begin("Select Map", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Available Maps:"); // Optional header
                ImGui.BeginChild("MapList", new Vector2(0, 150), true); // Scrollable region if list is long

                for (int i = 0; i < mapDefinitions.Length; i++)
                {
                    var map = mapDefinitions[i];
                    // Make each item selectable. The "##{i}" ensures a unique ID even if names are duplicated.
                    // The second argument makes the item appear selected if its index matches selectedMapIndex.
                    if (ImGui.Selectable($"{map.PrettyName}##{i}", selectedMapIndex == i))
                    {
                        selectedMapIndex = i;
                    }
                     
                    // Show details in a tooltip when hovering over the selectable item
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f); // Wrap tooltip text nicely
                        ImGui.Text($"File: {map.FileName}");
                        ImGui.Separator();
                        ImGui.TextWrapped($"Description: {map.Description}");
                        ImGui.Separator();
                        ImGui.Text($"Spawn Position: {map.SpawnPosition}");
                        ImGui.PopTextWrapPos();
                        ImGui.EndTooltip();
                    }
                }
                ImGui.EndChild(); // End scrollable region

                // Display details of the currently selected map below the list
                if (selectedMapIndex >= 0 && selectedMapIndex < mapDefinitions.Length)
                {
                    ImGui.Separator();
                    ImGui.Text("Selected Map Details:");
                    var selectedMap = mapDefinitions[selectedMapIndex];
                    ImGui.Text($"Name: {selectedMap.PrettyName}");
                    ImGui.Text($"File: {selectedMap.FileName}");
                    ImGui.TextWrapped($"Description: {selectedMap.Description}");
                    ImGui.Text($"Spawn: {selectedMap.SpawnPosition}");
                }

                ImGui.End();
            }

        }

        base.Render(dt, obj);
    }

    protected override void DisposeOther()
    {
        base.DisposeOther();

        Engine.ObjectManager.Textures.Remove("main_logo");
    }
}
