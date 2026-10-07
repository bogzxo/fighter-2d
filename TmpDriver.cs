// TMP-HOOK: test scaffolding, this whole file gets deleted before the work is handed over.
using Fighter2D.Content;
using Fighter2D.Map;
using Fighter2D.Match;
using Fighter2D.Networking;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Engine;
using Horizon.Input2;

namespace Fighter2D;

internal sealed class TmpDriver : Entity
{
    private readonly List<(float At, Action Do)> _steps = [];
    private readonly GameEngine _engine;
    private Gamepad _pad = null!;
    private MatchSetup? _setup;

    private GamepadSnapshot _held;
    private float _time, _releaseAt, _laterAt;
    private Action? _later;
    private int _next;

    private TmpDriver(GameEngine engine) => _engine = engine;

    public static void Attach(GameEngine engine)
    {
        string script = Environment.GetEnvironmentVariable("F2D_TMP") ?? string.Empty;
        if (script.Length == 0) return;

        var driver = new TmpDriver(engine) { _pad = GameInput.Manager.AddVirtualGamepad("tmp") };
        driver.Write(script);
        driver._steps.Sort((a, b) => a.At.CompareTo(b.At));

        engine.AddEntity(driver);
    }

    private void Press(float at, params GamepadInput[] inputs) =>
        _steps.Add((at, () => { _held = GamepadSnapshot.Holding(inputs); _releaseAt = _time + 0.07f; }));

    private void Do(float at, Action action) => _steps.Add((at, action));

    private void Write(string script)
    {
        switch (script)
        {
            case "host":
                Do(1.0f, () =>
                {
                    _setup = MatchSetup.Online(NetSession.Host());
                    _engine.SetScene(new GamepadSelectorScene(_setup));
                });
                Do(11.0f, () =>
                {
                    MapLoader.TryFind("japan", out MapDefinition map);
                    Console.WriteLine($"[tmp] host starts, peer present {_setup!.Lobby!.PeerPresent} verified {_setup.Lobby.Content.PeerVerified}");
                    _setup.Lobby.StartFight(map.FileName, _setup.Rules);
                    _engine.SetScene(_setup.CreateFight(map));
                });

                Do(15.0f, () => Fight.PlayerOne.Health = 60);

                // The file is changed where the host keeps its content, then the host tools read it again
                Do(16.0f, () =>
                {
                    string file = GameContent.PathOf(GameContent.CHARACTERS_FILE);
                    File.WriteAllText(file, File.ReadAllText(file).Replace("\"The Man\"", "\"The Bloke\""));
                    Console.WriteLine($"[tmp] changed {file}");
                });
                Press(17.0f, GamepadInput.Start);
                Press(17.5f, GamepadInput.DPadDown);
                Press(17.9f, GamepadInput.DPadDown);
                Press(18.4f, GamepadInput.A);

                // And later a plain pause, to see the countdown after it
                Press(27.0f, GamepadInput.Start);
                Press(28.0f, GamepadInput.B);

                for (int i = 0; i < 40; i++) Do(14.0f + i * 0.5f, Online);
                break;

            case "join":
                Do(1.0f, () =>
                {
                    _setup = MatchSetup.Online(NetSession.Join("127.0.0.1"));
                    _engine.SetScene(new GamepadSelectorScene(_setup));
                });
                // Slow on purpose. The reload the host asks for is sat on for two seconds, which the host has to wait out
                Do(14.0f, () =>
                {
                    if (Fight.Network?.ReloadRequested is not { } real) { Console.WriteLine("[tmp] no fight to dawdle in"); return; }
                    Fight.Network.ReloadRequested = () => { _later = real; _laterAt = _time + 2.0f; Console.WriteLine($"[tmp {_time:0.00}] told to reload, dawdling"); };
                });
                for (int i = 0; i < 40; i++) Do(14.0f + i * 0.5f, Online);
                break;

            case "solo":
                // Reload on one machine, off the pause menu. Carry on, start over, main menu, then the tools
                Do(5.5f, () => Fight.PlayerOne.Health = 60);
                Press(6.0f, GamepadInput.Start);
                Press(6.4f, GamepadInput.DPadDown);
                Press(6.7f, GamepadInput.DPadDown);
                Press(7.0f, GamepadInput.DPadDown);
                Press(7.6f, GamepadInput.A);
                for (int i = 0; i < 10; i++) Do(5.0f + i * 0.5f, Online);
                break;
        }
    }

    private void Online()
    {
        if (Fight.Round is not { } round || _engine.SceneManager.CurrentInstance is not FightScene)
        {
            Console.WriteLine($"[tmp {_time:0.00}] scene {_engine.SceneManager.CurrentInstance?.GetType().Name} peer {_setup?.Lobby?.PeerPresent}");
            return;
        }

        Console.WriteLine(
            $"[tmp {_time:0.00}] {round.Phase} {round.PhaseTime:0.0}s clock {round.TimeLeft:0.0} hp {Fight.PlayerOne.Health}/{Fight.PlayerTwo.Health} " +
            $"tick {Fight.PlayerOne.Controller.Tick} names {Fight.PlayerOne.Character?.PrettyName}/{Fight.PlayerTwo.Character?.PrettyName} x {Fight.PlayerOne.PhysicsBody?.Position.X:0}/{Fight.PlayerTwo.PhysicsBody?.Position.X:0} root '{GameContent.Root}'");
    }

    public override void UpdateState(float dt)
    {
        _time += dt;

        while (_next < _steps.Count && _steps[_next].At <= _time) _steps[_next++].Do();

        if (_later is not null && _time >= _laterAt)
        {
            Action later = _later;
            _later = null;
            later();
        }

        if (_time >= _releaseAt) _held = default;
        _pad.Update(in _held);

        base.UpdateState(dt);
    }
}
