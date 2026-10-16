using Horizon.Content;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Horizon.Engine;
using Horizon.Input;
using Horizon.Physics;
using Horizon.UI;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.Menus;

/// <summary>
/// Everything there is to settle before a fight, on the one screen. Who has which gamepad, who they play, where the
/// fight is and by what rules. It used to be four screens one after the other, and getting into a fight was a walk.
/// <para>
/// A fighter either side with whoever plays them underneath, the roster in the middle, the map and the rules under
/// that, and behind it all the map that's picked (see <see cref="MapPreview"/>). Any button on a gamepad takes the
/// next free corner (the gamepad that picked the fight on the main menu has player one's already), the d-pad walks
/// the roster, A locks a fighter in. Once everybody is locked in the rows under the roster are player one's, and A
/// there is the fight. The bumpers change the map at any time, Y is a gamepad's bindings, B takes back whatever
/// was done last.
/// </para>
/// <para>
/// A practice fight has the dummy in the other corner, X on the roster makes whoever you're on the dummy. An online
/// fight has whoever is on the other machine there, and this is its lobby (see FightSetupScene.Online.cs).
/// </para>
/// <para>
/// Every gamepad has a seat (<see cref="Seat"/>), not in yet, picking, locked in, or on the rules, and every action of
/// the screen says which seat it is for. That one table (see <see cref="BuildActions"/>) is both what the buttons do
/// and what the hint says, so the two can't drift apart the way fourteen hand written hints did.
/// </para>
/// </summary>
internal partial class FightSetupScene(MatchSetup setup) : MenuScene
{
    private const int GRID_ROWS_MIN = 2;
    // The row the bumpers flick through the maps of, by its name, wherever the layout has it
    private const string MAP_ROW = "map";

    // The colours of the two corners, the same ones the health bars of a fight have
    private static readonly Vector4 BlueCorner = new(0.42f, 0.66f, 1.0f, 1.0f);
    private static readonly Vector4 RedCorner = new(1.0f, 0.45f, 0.42f, 1.0f);

    // The map the last fight was on, by its file, so the next one starts on it
    private static string lastMap = string.Empty;

    // The panels cover the sides and the middle, what is left of the screen is its top and bottom, so the preview looks at the fight from where it is
    private static readonly Vector2 PreviewOffset = Vector2.Zero;

    protected override string LayoutFile => MenuLayouts.FIGHT_SETUP;
    protected override float InputDelay => 0.3f;

    /// <summary>What the text of a panel is coloured with when it wants somebody's attention.</summary>
    private readonly Pulse _fastPulse = new(0.3f, 1.0f, 1.05f), _slowPulse = new(0.3f, 1.0f, 2.1f);

    private List<CharacterDefinition> _characters = [];
    private List<MapDefinition> _maps = [];
    private MapPreview _preview = null!;

    private readonly FighterPanel[] _panels = new FighterPanel[2];
    private CharacterCell[] _cells = [];
    private int _columns = 1, _gridRows = GRID_ROWS_MIN;

    private readonly SettingList _match = new();
    private Label _subtitle = null!;
    private Button _fight = null!;
    private int _mapIndex;

    // Where every player of this machine is on the roster, and whether they have locked in
    private int[] _cursors = [];
    private bool[] _locked = [];

    // Whether the rows under the roster are player one's right now, which they are once everybody has locked in
    private bool _matchStage;

    private bool IsPractice => setup.Mode == MatchMode.Practice;

    protected override void Prepare()
    {
        _characters = CharacterDefinition.LoadAll();
        _maps = MapLoader.LoadAll();

        // Everybody starts on the character they had last time, or the first one
        _cursors = new int[setup.PlayerCount];
        _locked = new bool[setup.PlayerCount];
        for (int i = 0; i < _cursors.Length; i++)
        {
            _cursors[i] = Math.Max(0, _characters.FindIndex(character => character.Id == setup.GetCharacter(i)));
        }

        // Whoever the cursors start on is got ready for the fight now, on a worker, it's most likely who fights
        foreach (int cursor in _cursors)
        {
            if (cursor < _characters.Count) CharacterArt.PrepareAhead(_characters[cursor]);
        }
        if (IsPractice && Dummy is { } dummy) CharacterArt.PrepareAhead(dummy);

        _mapIndex = Math.Max(0, _maps.FindIndex(map => map.FileName == lastMap));
    }

    protected override void BuildBackdrop()
    {
        Vector2 viewport = Engine.WindowManager.ViewportSize;

        // The preview is a piece of the world, so the scene looks at it through a world camera the way a fight does.
        // The menu keeps the camera it was given, a UI doesn't care what the scene is looking at
        var worldCamera = AddEntity(new Camera2D(viewport / 2.0f));
        ActiveCamera = worldCamera;

        // Only there for the weather to live in, nothing ever collides with anything on this screen
        var world = AddComponent<PhysicsWorld>();

        _preview = Canvas.AddEntity(new MapPreview(worldCamera, world, viewport, PreviewOffset));
    }

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/fight_setup.hor, a corner in fighter_panel.hor and a cell of the roster in fighter_cell.hor
        layout.Get<Label>("title").Text = setup.Title;
        _subtitle = layout.Get<Label>("subtitle");

        _panels[0] = new FighterPanel(layout.Populate("left", 1)[0], mirrored: false);
        _panels[1] = new FighterPanel(layout.Populate("right", 1)[0], mirrored: true);

        // As many rows as it takes to get everybody in, the cells left over at the end are question marks
        var grid = layout.Get<GridPanel>("grid");
        _columns = Math.Max(1, grid.Columns);
        _gridRows = Math.Max(GRID_ROWS_MIN, (_characters.Count + _columns - 1) / _columns);

        var items = layout.Populate(grid, _columns * _gridRows);
        _cells = new CharacterCell[items.Count];
        for (int i = 0; i < _cells.Length; i++)
        {
            int index = i;
            _cells[i] = new CharacterCell(items[i], i < _characters.Count ? _characters[i] : null);

            // Clicking a cell is player one walking over and locking it in
            _cells[i].Box.OnPressed = () => ClickCell(index);
        }

        // The rows under the roster, which the gamepad of player one steps through once everybody is locked in. A click works any time
        MatchRules rules = setup.Rules;
        _match.Add(layout, "map", [.. _maps], _maps.Count > 0 ? _maps[_mapIndex] : default, map => map.PrettyName, ShowMap);
        _match.Add(layout, "rounds", MatchRules.RoundChoices, rules.RoundsToWin, MatchRules.DescribeRounds, rounds => rules.RoundsToWin = rounds);
        _match.Add(layout, "time", MatchRules.TimeChoices, rules.RoundSeconds, MatchRules.DescribeTime, seconds => rules.RoundSeconds = seconds);
        _match.Add(layout, "weather", MatchRules.WeatherChoices, rules.Weather, MatchRules.DescribeWeather, weather => rules.Weather = weather);
        _match.Dim();

        _fight = layout.Get<Button>("btn_fight");
        _fight.OnPressed = () =>
        {
            if (_matchStage) StartFight();
            else _fight.Shake(6.0f, 0.2f);
        };

        // For the mouse, a gamepad goes back with B
        layout.Get<Button>("btn_back").OnPressed = Back;

        if (_maps.Count > 0) _preview.Show(_maps[_mapIndex]);

        BuildLobby(layout);
    }

    protected override void UpdateMenu(float dt)
    {
        _fastPulse.Update(dt);
        _slowPulse.Update(dt);

        foreach (FighterPanel panel in _panels) panel.Update(dt);
        foreach (CharacterCell cell in _cells) cell.Update(dt);

        // The lobby can close on us or the host can start the fight, either of which is the end of this screen
        if (UpdateLobby()) return;

        DropUnplugged();
        UpdateStage();

        ShowPanels();
        ShowCursors();
        ShowSubtitle();
    }

    /* The gamepads */

    /// <summary>
    /// Where a gamepad is on this screen. Not anybody's yet, a player walking the roster, a player who has locked in
    /// (online, who is ready), or player one with the map and the rules once everybody has.
    /// </summary>
    private enum Seat
    {
        Out,
        Picking,
        Locked,
        Rules
    }

    private Seat SeatOf(Gamepad gamepad)
    {
        int player = setup.Slots.IndexOf(gamepad.Slot);
        if (player < 0) return Seat.Out;
        if (player == 0 && _matchStage) return Seat.Rules;
        return IsLocked(player) ? Seat.Locked : Seat.Picking;
    }

    private int PlayerOf(Gamepad gamepad) => setup.Slots.IndexOf(gamepad.Slot);

    private bool Is(Gamepad gamepad, Seat seat) => SeatOf(gamepad) == seat;
    private bool IsIn(Gamepad gamepad) => SeatOf(gamepad) != Seat.Out;

    protected override void BuildActions()
    {
        bool online = setup.IsOnline;

        // What the hint says before any button, the waits of a lobby
        if (online)
        {
            Actions.Add(new MenuAction { Text = "hints.find_you", Says = () => Localisation.Format("hints.find_you", ("addresses", _hostAddresses)), When = gamepad => IsIn(gamepad) && _lobby is { PeerPresent: false } });
            Actions.Note("hints.host_choosing", gamepad => Is(gamepad, Seat.Locked) && _lobby is { BothReady: true, IsHost: false, ChoosingMap: true });
            Actions.Note("hints.waiting_for_host", gamepad => Is(gamepad, Seat.Locked) && _lobby is { BothReady: true, IsHost: false, ChoosingMap: false });
            Actions.Note("hints.waiting_for_them", gamepad => Is(gamepad, Seat.Locked) && _lobby is { BothReady: false });
        }
        else Actions.Note("hints.waiting_for_everybody", gamepad => Is(gamepad, Seat.Locked));

        // A gamepad nobody has yet. B with nobody in is back like everywhere else, any other button takes the next corner
        Actions.Add(GamepadInput.B, "hints.back", _ => Back(), gamepad => Is(gamepad, Seat.Out) && setup.Slots.Count == 0);
        Actions.Any("hints.press_any_to_join", Join, gamepad => Is(gamepad, Seat.Out) && !setup.IsReady && GameInput.Manager.ConnectedCount > setup.Slots.Count);
        Actions.Note("hints.plug_in", gamepad => !setup.IsReady && GameInput.Manager.ConnectedCount <= setup.Slots.Count);

        // Player one on the rows under the roster, once everybody is locked in. Up and down pick a row, left and right change it
        Actions.Directions("hints.choose_and_change", (_, right, down) =>
        {
            if (down != 0) _match.Move(down);
            else _match.Step(right);
        }, gamepad => Is(gamepad, Seat.Rules));
        Actions.Add(GamepadInput.A, "hints.fight", _ => StartFight(), gamepad => Is(gamepad, Seat.Rules));

        // Walking the roster and locking in. Online locking in is saying you're ready, which takes somebody to be ready
        // for and the files to fight with, and a press without them says why instead
        Actions.Directions("hints.choose", (gamepad, right, down) => Move(PlayerOf(gamepad), right, down), gamepad => Is(gamepad, Seat.Picking));
        Actions.Add(GamepadInput.A, online ? "hints.ready" : "hints.lock_in", gamepad => Lock(PlayerOf(gamepad)), gamepad => Is(gamepad, Seat.Picking) && CanBeReady(out _));
        Actions.Add(new MenuAction { Button = GamepadInput.A, Shown = false, Pressed = gamepad => Refuse(PlayerOf(gamepad)), When = gamepad => Is(gamepad, Seat.Picking) && !CanBeReady(out _) });

        // X makes whoever you're on the dummy in practice, and swaps the corners online
        if (IsPractice) Actions.Add(GamepadInput.X, "hints.make_them_the_dummy", gamepad => MakeDummy(_cursors[PlayerOf(gamepad)]), gamepad => Is(gamepad, Seat.Picking));
        if (online) Actions.Add(GamepadInput.X, "hints.switch_corners", _ => SwitchCorners(), gamepad => Is(gamepad, Seat.Picking) && _lobby is { PeerPresent: true });

        // The map is one press away for whoever drives the menus, wherever they are on the screen
        Actions.Pair(GamepadInput.LeftBumper, GamepadInput.RightBumper, "hints.map", (_, step) => _match.Step(_match.IndexOf(MAP_ROW), step),
            gamepad => PlayerOf(gamepad) == 0 && CanChangeMatch);

        Actions.Add(GamepadInput.Y, "hints.bindings", gamepad => OpenBindings(gamepad.Slot), IsIn);

        // B takes back whatever was done last. Off the rules back to the fighters, a lock in, and from the roster player
        // one leaving is everybody leaving and anybody after them only gives their corner back
        Actions.Add(GamepadInput.B, "hints.back_to_fighters", _ => Unlock(0), gamepad => Is(gamepad, Seat.Rules));
        Actions.Add(GamepadInput.B, online ? "hints.not_ready" : "hints.change_your_mind", gamepad => Unlock(PlayerOf(gamepad)), gamepad => Is(gamepad, Seat.Locked));
        Actions.Add(GamepadInput.B, online ? "hints.leave" : "hints.back", _ => Back(), gamepad => Is(gamepad, Seat.Picking) && PlayerOf(gamepad) == 0);
        Actions.Add(GamepadInput.B, "hints.leave", gamepad => Leave(PlayerOf(gamepad)), gamepad => Is(gamepad, Seat.Picking) && PlayerOf(gamepad) > 0);
    }

    /// <summary>
    /// A gamepad nobody had takes the next corner. Its panel jumps, so there is no doubt it went to them.
    /// </summary>
    private void Join(Gamepad gamepad)
    {
        int player = setup.Slots.Count;
        setup.Slots.Add(gamepad.Slot);
        PanelOf(player).Bump(0.1f);
    }

    /// <summary>
    /// A press of lock in that can't be one yet, online with nobody to be ready for or without the files. Says why.
    /// </summary>
    private void Refuse(int player)
    {
        if (CanBeReady(out string why)) return;

        Hint?.Say(why);
        PanelOf(player).Box.Shake(6.0f, 0.2f);
    }

    /// <summary>
    /// Helper method to take a gamepad away from its player when it is pulled out, along with everybody who joined after them.
    /// </summary>
    private void DropUnplugged()
    {
        for (int i = 0; i < setup.Slots.Count; i++)
        {
            if (GameInput.Manager.TryGet(setup.Slots[i], out Gamepad gamepad) && gamepad.IsConnected) continue;

            Leave(i);
            return;
        }
    }

    /// <summary>
    /// A player gives their corner back, and so does everybody who joined after them, the corners go by who joined when.
    /// </summary>
    private void Leave(int player)
    {
        for (int i = player; i < _locked.Length; i++) Unlock(i);

        setup.Slots.RemoveRange(player, setup.Slots.Count - player);
        PanelOf(player).Box.Shake(8.0f);
    }

    private void OpenBindings(int slot)
    {
        // Nobody is ready while somebody is off changing buttons, online the host can't start the fight under them.
        // The screen is opened over this one, so who everybody was on is still here when they are back
        for (int i = 0; i < _cursors.Length; i++) Unlock(i);

        int player = Math.Max(0, setup.Slots.IndexOf(slot));
        Open(new BindingsScene(slot, Localisation.Format("bindings.title_player", ("number", player + 1))), Screen.BetweenMenus);
    }

    /* The roster */

    private void Move(int player, int column, int row)
    {
        int x = Math.Clamp(_cursors[player] % _columns + column, 0, _columns - 1);
        int y = Math.Clamp(_cursors[player] / _columns + row, 0, _gridRows - 1);
        int target = y * _columns + x;

        // There is nobody in the empty cells to walk over to
        if (_cells[target].Character is null)
        {
            _cells[target].Box.Shake(4.0f, 0.2f);
            return;
        }

        _cursors[player] = target;

        // Got ready while they make up their mind, a fight is over a second quicker to start for it
        CharacterArt.PrepareAhead(_cells[target].Character!);

        // Whoever is on the other machine sees who we are looking at, not only who we end up with
        if (setup.IsOnline && _cells[target].Character is { } character) setup.SetCharacter(player, character.Id);
    }

    private void Lock(int player)
    {
        CharacterCell cell = _cells[_cursors[player]];
        if (cell.Character is not { } character) return;

        // Online being locked in is being ready, which takes somebody to be ready for and the files to fight with
        if (!CanBeReady(out _))
        {
            Refuse(player);
            return;
        }

        setup.SetCharacter(player, character.Id);
        SetLocked(player, true);

        cell.Box.Punch(0.1f, 0.3f);
        PanelOf(player).Bump();
    }

    private void Unlock(int player)
    {
        if (IsLocked(player)) SetLocked(player, false);
    }

    private void ClickCell(int index)
    {
        if (_cells[index].Character is null || setup.Slots.Count == 0 || IsLocked(0)) return;

        _cursors[0] = index;
        Lock(0);
    }

    /// <summary>
    /// The dummy of a practice fight is whoever player one says it is, X on the roster. It stays that for the rematch.
    /// </summary>
    private void MakeDummy(int index)
    {
        if (_cells[index].Character is not { } character) return;

        setup.DummyCharacter = character.Id;
        CharacterArt.PrepareAhead(character);
        _panels[1].Bump();
    }

    private CharacterDefinition? Dummy =>
        _characters.Find(character => character.Id == setup.DummyCharacter) ?? (_characters.Count > 0 ? _characters[0] : null);

    /* The map and the rules */

    /// <summary>
    /// Whether everybody of this machine has a gamepad and a fighter locked in.
    /// </summary>
    private bool EverybodyLocked
    {
        get
        {
            if (!setup.IsReady) return false;

            for (int i = 0; i < _locked.Length; i++)
            {
                if (!IsLocked(i)) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Helper method to hand the rows under the roster to player one once everybody is locked in, and take them back when somebody isn't any more.
    /// </summary>
    private void UpdateStage()
    {
        bool ready = EverybodyLocked && CanStartFight;
        if (ready == _matchStage) return;

        _matchStage = ready;
        ChoosingMatch(ready);

        if (ready)
        {
            // Onto the fight button's doorstep, A straight away is the fight on what was there last time
            _match.Select(_match.Selected);
            _fight.Punch(0.08f, 0.3f);
        }
        else _match.Dim();
    }

    private void ShowMap(MapDefinition map)
    {
        _mapIndex = Math.Max(0, _maps.FindIndex(each => each.FileName == map.FileName));
        _preview.Show(map);
    }

    private void StartFight()
    {
        if (_maps.Count == 0 || !_matchStage) return;

        MapDefinition map = _maps[_mapIndex];
        lastMap = map.FileName;

        AnnounceFight(map);
        GoTo(setup.CreateFight(map), Screen.IntoFight);
    }

    /// <summary>
    /// Goes back to the main menu, which for an online fight means hanging up on whoever is in the lobby.
    /// </summary>
    private void Back()
    {
        setup.Close();
        GoTo(new MainMenuScene());
    }

    /* What is on the screen */

    /// <summary>The panel of a player of this machine. Online that is whichever corner we are standing in.</summary>
    private FighterPanel PanelOf(int player) => _panels[CornerOf(player)];

    private void ShowPanels()
    {
        for (int player = 0; player < _cursors.Length; player++)
        {
            ShowPlayer(PanelOf(player), player);
        }

        if (setup.IsOnline) ShowOpponent(_panels[1 - CornerOf(0)]);
        else if (IsPractice) ShowDummy(_panels[1]);
    }

    private void ShowPlayer(FighterPanel panel, int player)
    {
        int corner = CornerOf(player);
        panel.ShowTitle(setup.IsOnline ? Localisation.Get("setup.you") : Localisation.Format("common.player", ("number", player + 1)), corner == 0 ? BlueCorner : RedCorner);

        if (player < setup.Slots.Count && GameInput.Manager.TryGet(setup.Slots[player], out Gamepad gamepad))
        {
            panel.Show(_cells[_cursors[player]].Character);

            // Holding anything turns the name white, which is how you check the gamepad in your hands is the one that joined
            panel.ShowStatus(FighterPanel.Shorten(gamepad.Name), gamepad.AnyDown ? Vector4.One : MenuColors.Name);

            if (IsLocked(player)) panel.ShowDetail(Localisation.Get("setup.locked_in"), MenuColors.Ready);
            else panel.ShowDetail(DescribeWaiting(gamepad), MenuColors.Hint);
        }
        else if (player == setup.Slots.Count)
        {
            panel.Show(null);
            panel.ShowStatus(Localisation.Get("setup.press_any_button"), _fastPulse.Grey);
            panel.ShowDetail(Localisation.Get("setup.on_your_gamepad"), MenuColors.Hint);
        }
        else
        {
            panel.Show(null);
            panel.ShowStatus("waiting", MenuColors.Hint);
            panel.ShowDetail(Localisation.Format("setup.for_player", ("number", player)), MenuColors.Hint);
        }
    }

    private void ShowDummy(FighterPanel panel)
    {
        panel.ShowTitle(Localisation.Get("setup.the_dummy"), RedCorner);
        panel.Show(Dummy);
        panel.ShowStatus(Localisation.Get("setup.dummy_takes_it"), MenuColors.Hint);

        GameInput.Manager.TryGet(setup.MenuSlot, out Gamepad? gamepad);
        panel.ShowDetail(ButtonGlyphs.Localize(Localisation.Get("setup.dummy_swap"), gamepad), MenuColors.Hint);
    }

    /// <summary>
    /// Helper method to show who is on which cell of the roster.
    /// </summary>
    private void ShowCursors()
    {
        var tags = new StringBuilder();
        string? dummy = IsPractice ? Dummy?.Id : null, opponent = OpponentCharacter;

        for (int i = 0; i < _cells.Length; i++)
        {
            tags.Clear();
            bool everybodyLocked = true, anybody = false;

            for (int player = 0; player < _cursors.Length && player < setup.Slots.Count; player++)
            {
                if (_cursors[player] != i) continue;

                if (tags.Length > 0) tags.Append(' ');
                tags.Append(setup.IsOnline ? Localisation.Get("setup.tag_you") : Localisation.Format("setup.tag_player", ("number", player + 1)));

                anybody = true;
                everybodyLocked &= IsLocked(player);
            }

            // Whoever isn't picked by a gamepad of this machine is on the roster as well, a tag and no cursor
            string? id = _cells[i].Character?.Id;
            if (id is not null && id == dummy) tags.Append(tags.Length > 0 ? " " : "").Append(Localisation.Get("setup.tag_dummy"));
            if (id is not null && id == opponent) tags.Append(tags.Length > 0 ? " " : "").Append(Localisation.Get("setup.tag_them"));

            // Green once everybody standing on the cell has locked it in
            _cells[i].ShowPlayers(tags.ToString(), anybody && everybodyLocked ? MenuColors.Ready : Vector4.One, lit: anybody);
        }
    }

    private void ShowSubtitle()
    {
        _subtitle.Text = Localisation.Get(_matchStage ? "setup.where_and_rules" : EverybodyLocked ? "setup.everybody_locked" : "setup.pick_fighters");
    }
}
