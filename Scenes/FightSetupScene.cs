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

namespace Fighter2D.Scenes;

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
/// </summary>
internal partial class FightSetupScene(MatchSetup setup) : MenuScene
{
    private const int GRID_ROWS_MIN = 2;
    private const int MAP_ROW = 0;

    private const string HINT_JOIN = "press any button on a gamepad to join    [icon:pad_b] back";
    private const string HINT_PICK = "[icon:dpad] choose    [icon:pad_a] lock in    [icon:pad_lb] [icon:pad_rb] map    [icon:pad_y] bindings    [icon:pad_b] back";
    private const string HINT_PICK_PRACTICE = "[icon:dpad] choose    [icon:pad_a] lock in    [icon:pad_x] make them the dummy    [icon:pad_lb] [icon:pad_rb] map    [icon:pad_y] bindings    [icon:pad_b] back";
    private const string HINT_WAIT = "waiting for everybody to lock in    [icon:pad_b] change your mind";
    private const string HINT_MATCH = "[icon:dpad] choose and change    [icon:pad_a] fight    [icon:pad_b] back to the fighters";

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
    private Label _hint = null!, _subtitle = null!;
    private Button _fight = null!;
    private int _mapIndex;

    // Where every player of this machine is on the roster, and whether they have locked in
    private int[] _cursors = [];
    private bool[] _locked = [];

    // Whether the rows under the roster are player one's right now, which they are once everybody has locked in
    private bool _matchStage;

    // Something the hint has to say for a moment in place of which buttons to press
    private string _message = string.Empty;
    private float _messageTime;

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
        _hint = layout.Get<Label>("hint");

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
        if (_messageTime > 0.0f) _messageTime -= dt;

        foreach (FighterPanel panel in _panels) panel.Update(dt);
        foreach (CharacterCell cell in _cells) cell.Update(dt);

        // The lobby can close on us or the host can start the fight, either of which is the end of this screen
        if (UpdateLobby()) return;

        DropUnplugged();
        UpdateStage();

        ShowPanels();
        ShowCursors();
        ShowHint();

        if (!InputReady) return;

        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            // Only one thing happens per update, whoever pressed first wins
            int player = setup.Slots.IndexOf(gamepad.Slot);
            if (player < 0 ? HandleNewcomer(gamepad) : HandlePlayer(player, gamepad)) return;
        }
    }

    /* The gamepads */

    /// <summary>
    /// Called for every gamepad nobody has yet, returns whether it did anything.
    /// </summary>
    private bool HandleNewcomer(Gamepad gamepad)
    {
        if (gamepad.Pressed is not { } input) return false;

        // B with nobody in is back like everywhere else, with somebody in it's theirs to press
        if (input == GamepadInput.B && setup.Slots.Count == 0)
        {
            Back();
            return true;
        }

        if (setup.IsReady) return false;

        // Any button takes the next corner. Its panel jumps, so there is no doubt it went to them
        int player = setup.Slots.Count;
        setup.Slots.Add(gamepad.Slot);
        PanelOf(player).Bump(0.1f);
        return true;
    }

    /// <summary>
    /// Called for every gamepad that is somebody's, returns whether it did anything.
    /// </summary>
    private bool HandlePlayer(int player, Gamepad gamepad)
    {
        if (gamepad.WasPressed(GamepadInput.Y))
        {
            OpenBindings(gamepad.Slot);
            return true;
        }

        // The map is one press away for whoever drives the menus, wherever they are on the screen
        if (player == 0 && CanChangeMatch && (gamepad.WasPressed(GamepadInput.LeftBumper) || gamepad.WasPressed(GamepadInput.RightBumper)))
        {
            _match.Step(MAP_ROW, gamepad.WasPressed(GamepadInput.RightBumper) ? 1 : -1);
            return true;
        }

        if (_matchStage && player == 0) return HandleMatch(gamepad);

        if (IsLocked(player))
        {
            // B on a fighter that is locked in only unlocks them
            if (gamepad.WasPressed(GamepadInput.B))
            {
                Unlock(player);
                return true;
            }

            return false;
        }

        if (gamepad.WasPressed(GamepadInput.B))
        {
            // Player one leaving is everybody leaving, anybody after them only gives their corner back
            if (player == 0) Back();
            else Leave(player);
            return true;
        }

        int column = MenuInput.Horizontal(gamepad), row = MenuInput.Vertical(gamepad);
        if (column != 0 || row != 0)
        {
            Move(player, column, row);
            return true;
        }

        if (MenuInput.ConfirmPressed(gamepad))
        {
            Lock(player);
            return true;
        }

        if (gamepad.WasPressed(GamepadInput.X))
        {
            if (IsPractice) MakeDummy(_cursors[player]);
            else SwitchCorners();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Called for player one's gamepad while the rows under the roster are theirs.
    /// </summary>
    private bool HandleMatch(Gamepad gamepad)
    {
        int vertical = MenuInput.Vertical(gamepad), horizontal = MenuInput.Horizontal(gamepad);

        if (MenuInput.ConfirmPressed(gamepad)) StartFight();
        else if (gamepad.WasPressed(GamepadInput.B)) Unlock(0);
        else if (vertical != 0) _match.Move(vertical);
        else if (horizontal != 0) _match.Step(horizontal);
        else return false;

        return true;
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
        // Who everybody was on is kept for when we are back, nobody is ready while somebody is off changing buttons
        for (int i = 0; i < _cursors.Length; i++)
        {
            Unlock(i);
            if (_cells[_cursors[i]].Character is { } character) setup.SetCharacter(i, character.Id);
        }

        int player = Math.Max(0, setup.Slots.IndexOf(slot));
        GoTo(new BindingsScene(slot, $"Bindings of player {player + 1}", () => new FightSetupScene(setup)));
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

        // Whoever is on the other machine sees who we are looking at, not only who we end up with
        if (setup.IsOnline && _cells[target].Character is { } character) setup.SetCharacter(player, character.Id);
    }

    private void Lock(int player)
    {
        CharacterCell cell = _cells[_cursors[player]];
        if (cell.Character is not { } character) return;

        // Online being locked in is being ready, which takes somebody to be ready for and the files to fight with
        if (!CanBeReady(out string why))
        {
            Say(why);
            PanelOf(player).Box.Shake(6.0f, 0.2f);
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
        panel.ShowTitle(setup.IsOnline ? "You" : $"Player {player + 1}", corner == 0 ? BlueCorner : RedCorner);

        if (player < setup.Slots.Count && GameInput.Manager.TryGet(setup.Slots[player], out Gamepad gamepad))
        {
            panel.Show(_cells[_cursors[player]].Character);

            // Holding anything turns the name white, which is how you check the gamepad in your hands is the one that joined
            panel.ShowStatus(FighterPanel.Shorten(gamepad.Name), gamepad.AnyDown ? Vector4.One : MenuColors.Name);

            if (IsLocked(player)) panel.ShowDetail("locked in!", MenuColors.Ready);
            else panel.ShowDetail(DescribeWaiting(gamepad), MenuColors.Hint);
        }
        else if (player == setup.Slots.Count)
        {
            panel.Show(null);
            panel.ShowStatus("press any button", _fastPulse.Grey);
            panel.ShowDetail("on your gamepad", MenuColors.Hint);
        }
        else
        {
            panel.Show(null);
            panel.ShowStatus("waiting", MenuColors.Hint);
            panel.ShowDetail($"for player {player}", MenuColors.Hint);
        }
    }

    private void ShowDummy(FighterPanel panel)
    {
        panel.ShowTitle("The dummy", RedCorner);
        panel.Show(Dummy);
        panel.ShowStatus("stands there and takes it", MenuColors.Hint);

        GameInput.Manager.TryGet(setup.MenuSlot, out Gamepad? gamepad);
        panel.ShowDetail(ButtonGlyphs.Localize("[icon:pad_x] on the roster swaps them", gamepad), MenuColors.Hint);
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
                tags.Append(setup.IsOnline ? "YOU" : $"P{player + 1}");

                anybody = true;
                everybodyLocked &= IsLocked(player);
            }

            // Whoever isn't picked by a gamepad of this machine is on the roster as well, a tag and no cursor
            string? id = _cells[i].Character?.Id;
            if (id is not null && id == dummy) tags.Append(tags.Length > 0 ? " " : "").Append("DUMMY");
            if (id is not null && id == opponent) tags.Append(tags.Length > 0 ? " " : "").Append("THEM");

            // Green once everybody standing on the cell has locked it in
            _cells[i].ShowPlayers(tags.ToString(), anybody && everybodyLocked ? MenuColors.Ready : Vector4.One, lit: anybody);
        }
    }

    private void ShowHint()
    {
        GameInput.Manager.TryGet(setup.MenuSlot, out Gamepad? first);
        Gamepad? gamepad = GameInput.Manager.LastUsed ?? first;

        _subtitle.Text = _matchStage ? "where, and by what rules" : EverybodyLocked ? "everybody is locked in" : "pick your fighters";

        if (_messageTime > 0.0f)
        {
            _hint.Text = _message;
            _hint.Color = MenuColors.Error;
            return;
        }

        _hint.Color = MenuColors.Hint;
        _hint.Text = ButtonGlyphs.Localize(DescribeHint(), gamepad);
    }

    private string DescribeHint()
    {
        if (LobbyHint is { } lobby) return lobby;
        if (_matchStage) return HINT_MATCH;

        if (!setup.IsReady)
        {
            return GameInput.Manager.ConnectedCount <= setup.Slots.Count ? "Plug in a gamepad to continue    [icon:pad_b] back" : HINT_JOIN;
        }

        if (EverybodyLocked || IsLocked(0)) return HINT_WAIT;
        return IsPractice ? HINT_PICK_PRACTICE : HINT_PICK;
    }

    /// <summary>
    /// Helper method to have the hint say something else for a couple of seconds, why something that was pressed did nothing.
    /// </summary>
    private void Say(string message)
    {
        _message = message;
        _messageTime = 2.5f;
        _hint.Shake(6.0f);
    }
}
