using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Text;
using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Horizon.Engine;
using ImGuiNET;

namespace Fighter2D.Scenes;

internal class JoinServerGameScreen(MapLoader.MapDefinition mapDefinition, int gamepadIndex) : Scene
{
    public override Camera ActiveCamera { get; protected set; }
    private byte[] _ipAddrBuffer = new byte[16];
    private bool invalidAddr = false;

    public override void Initialize()
    {
        ActiveCamera = AddEntity(new Camera2D(new Vector2(1600, 900)));

        base.Initialize();
    }

    public override void Render(float dt, object? obj = null)
    {
        if (ImGui.Begin("Enter IP"))
        {
            ImGui.InputText("- IP Addr", _ipAddrBuffer, 16);

            if (invalidAddr) ImGui.Text("dude cmon.");
            if (ImGui.Button("Connect"))
            {
                string text = Encoding.UTF8.GetString(_ipAddrBuffer).TrimEnd('\0');
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

            ImGui.End();
        }
        base.Render(dt, obj);
    }
}