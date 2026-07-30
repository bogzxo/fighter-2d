using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Horizon.Engine;
using Horizon.Rendering.Text;

using Silk.NET.Input;

namespace Fighter2D.Scenes;

internal class MapSelectionScene : Scene
{
    public override Camera ActiveCamera { get; protected set; }
    private Camera2D camera;


    private MapLoader.MapDefinition[] mapDefinitions;
    private GlyphRenderer glyphRenderer;
    private int selectedIndex = 0;
    private IGamepad? _gamepad;
    private float totalEngineTime = 0;

    public override void Initialize()
    {
        mapDefinitions = [.. MapLoader.LoadDefinitions("Assets/data/maps.hor")];

        ActiveCamera = camera = AddEntity<Camera2D>(new(Engine.WindowManager.WindowSize));
        Engine.GL.ClearColor(System.Drawing.Color.PaleVioletRed);

        glyphRenderer = AddEntity(new GlyphRenderer("Assets/fonts/FIGHTERFISH", "FIGHTERFISH.fnt"));
        glyphRenderer.Initialize();
        glyphRenderer.AddLabel("title", new TextLabel
        {
            Text = "Select  Map",
            Origin = Horizon.Rendering.Origin.Bottom,
            Transform = {
                Position = new Vector2(0, Engine.WindowManager.WindowSize.Y / 2)
            }
        });


        float offsetY = 0;
        for (int i = 0; i < mapDefinitions.Length; i++)
        {
            var map = mapDefinitions[i];

            glyphRenderer.AddLabel(map.FileName, new TextLabel
            {
                Text = map.PrettyName,
                Origin = Horizon.Rendering.Origin.Center,
                Transform = {
                    Position = new Vector2(0, offsetY)
                }
            });

            offsetY -= glyphRenderer.CalculateSize(map.PrettyName).Y;
        }

        // This forces the first map to be shown as selected
        UpdateLabelTexts();

        base.Initialize();
    }

    public override void UpdateState(float dt)
    {
        totalEngineTime += dt;

        // Attach a gamepad
        if (_gamepad is null && GameEngine.Instance.InputManager.NativeInputContext?.Gamepads.Count > 0)
        {
            _gamepad = GameEngine.Instance.InputManager.NativeInputContext.Gamepads[0];
            _gamepad.ButtonDown += (_, args) =>
            {
                HandleDpadNavigation(args.Name);
            };
        }

        // Process Scene Transition on the MAIN THREAD safely
        if (totalEngineTime > 1.0f && Engine.InputManager.IsPressed(Horizon.Input.VirtualAction.Interact))
        {
            Engine.SetScene(new Program(mapDefinitions[selectedIndex]));
            return;
        }

        base.UpdateState(dt);
    }

    private void HandleDpadNavigation(ButtonName name)
    {
        if (name == ButtonName.DPadDown)
        {
            if (selectedIndex + 1 < mapDefinitions.Length)
                selectedIndex++;
        }
        else if (name == ButtonName.DPadUp)
        {
            if (selectedIndex - 1 >= 0)
                selectedIndex--;
        }

        UpdateLabelTexts();
    }

    private void UpdateLabelTexts()
    {
        for (int i = 0; i < mapDefinitions.Length; i++)
        {
            if (i == selectedIndex)
                glyphRenderer[mapDefinitions[i].FileName].Text = "> " + mapDefinitions[i].PrettyName;
            else
                glyphRenderer[mapDefinitions[i].FileName].Text = mapDefinitions[i].PrettyName;
        }

        glyphRenderer.SetDirty();
    }
}
