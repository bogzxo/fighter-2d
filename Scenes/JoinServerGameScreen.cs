using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Text;
using Egui;
using Egui.Containers;
using Egui.Widgets;
using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Horizon.Engine;

namespace Fighter2D.Scenes;

internal class JoinServerGameScreen(MapLoader.MapDefinition mapDefinition, int gamepadIndex) : Horizon.Engine.Scene
{
    public override Camera ActiveCamera { get; protected set; } = null!;
    private string _ipAddress = "127.0.0.1";
    private bool invalidAddr = false;

    public override void Initialize()
    {
        ActiveCamera = AddEntity(new Camera2D(new Vector2(1600, 900)));

        base.Initialize();
    }

    public override void RenderUi(Ui root)
    {
        new Window("Enter IP")
            .Show(root.Ctx, ui =>
            {
                ui.Label("- IP Addr:");
                ui.TextEditSingleline(ref _ipAddress);

                if (invalidAddr) ui.Label("dude cmon.");
                if (ui.Button("Connect").Clicked)
                {
                    string text = _ipAddress.Trim();
                    if (!IPAddress.TryParse(text, out _))
                    {
                        invalidAddr = true;
                    }
                    else
                    {
                        Engine.SetScene(new FightScene(mapDefinition, gamepadIndex, new NetworkPlayer(text)
                        {
                            SpawnPosition = new(mapDefinition.SpawnPosition.X * 16 + 256, TileMapChunk.HEIGHT * 16 - mapDefinition.SpawnPosition.Y * 16),
                        }));
                    }
                }
            });

        base.RenderUi(root);
    }
}