using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

using Fighter2D.Networking;
using Horizon.Engine;
using Horizon.Input2;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the players pick their gamepads, one after the other: player one presses any button on the gamepad they want, then player two does.
/// A gamepad that has been picked can change its bindings from here as well, see <see cref="BindingsScene"/>.
/// For an online fight this is the lobby too: the host waits here for the other player, both pick a corner and say they are ready,
/// and the host goes on to choose the map while the other player stays here until the fight starts.
/// </summary>
internal class GamepadSelectorScene(MatchSetup setup) : Scene
{
    private const int MAX_NAME_LENGTH = 22;
    private const float INPUT_DELAY = 0.25f;

    private const string TEXT_WAITING = "press any button";
    private const string HINT_READY = "[icon:pad_a] continue    [icon:pad_y] bindings    [icon:pad_b] undo";
    private const string HINT_CORNER = "[icon:dpad] switch corners    [icon:pad_a] ready";

    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);
    private static readonly Vector4 NameColor = new(0.0f, 0.86f, 1.0f, 1.0f);
    private static readonly Vector4 ReadyColor = new(0.33f, 0.85f, 0.45f, 1.0f);
    private static readonly Vector4 ErrorColor = new(1.0f, 0.45f, 0.4f, 1.0f);

    /// <summary>
    /// Everything on screen that belongs to one player.
    /// </summary>
    private sealed class Card
    {
        public StackPanel Box = null!;
        public Label Status = null!, Detail = null!;
    }

    public override Camera ActiveCamera { get; protected set; }

    // The glass everything is seen through
    private Renderer2D screen = null!;

    private Card[] cards = [];
    private StackPanel panel;
    private Label hint;
    private float _delayTimer = 0.0f;
    private float totalTime = 0.0f;

    // The lobby as it was when we last looked, so the cards can jump when something about it changes
    private int lobbyRevision;
    private bool lobbyHadPeer;
    private string hostAddresses = string.Empty;

    public override void Initialize()
    {
        ActiveCamera = AddEntity(new Camera2D(Engine.WindowManager.WindowSize));
        Engine.GL.ClearColor(System.Drawing.Color.PaleVioletRed);

        screen = Screen.For(this);

        CompositeImages();
        CompositeUi();

        if (setup.Lobby is { } lobby)
        {
            hostAddresses = NetSession.DescribeLocalAddresses();

            // The host answers with how things stand in the lobby (and is back from choosing the map, if that is where we come from)
            lobby.SayHello();
            lobby.SetChoosingMap(false);
            lobby.SetCharacter(setup.GetCharacter(0));

            lobbyRevision = lobby.Revision;
            lobbyHadPeer = lobby.PeerPresent;
        }

        base.Initialize();
    }

    public override void UpdateState(float dt)
    {
        _delayTimer += dt;
        totalTime += dt;

        DropUnplugged();

        if (setup.Lobby is { } lobby)
        {
            if (UpdateLobby(lobby)) return;
            UpdateLobbyCards(lobby);
        }
        else
        {
            UpdateCards();
        }

        base.UpdateState(dt);

        // The button that got us here is most likely still held, dont let it pick a gamepad straight away
        if (_delayTimer < INPUT_DELAY) return;

        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            // Only one thing happens per update, whoever pressed first wins
            if (setup.Slots.Contains(gamepad.Slot) ? UpdatePicked(gamepad) : UpdateUnpicked(gamepad)) return;
        }
    }

    /// <summary>
    /// Called for every gamepad that belongs to a player already, returns whether it did anything.
    /// </summary>
    private bool UpdatePicked(Gamepad gamepad)
    {
        if (gamepad.WasPressed(GamepadInput.Y))
        {
            Engine.SetScene(new BindingsScene(gamepad.Slot, setup));
            return true;
        }

        if (setup.Lobby is { } lobby)
        {
            return UpdatePickedOnline(gamepad, lobby);
        }

        if (gamepad.WasPressed(GamepadInput.B))
        {
            // The last player to pick goes back to picking
            setup.Slots.RemoveAt(setup.Slots.Count - 1);
            cards[setup.Slots.Count].Box.Shake(8.0f);
            return true;
        }

        if (setup.IsReady && gamepad.WasPressed(GamepadInput.A))
        {
            // Everybody has a gamepad, next they pick who they play
            Engine.SetScene(new CharacterSelectScene(setup));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Called for our gamepad in the lobby of an online fight, returns whether it did anything.
    /// </summary>
    private bool UpdatePickedOnline(Gamepad gamepad, OnlineLobby lobby)
    {
        if (gamepad.WasPressed(GamepadInput.B))
        {
            // One step back at a time: not ready any more first, then the gamepad goes back to being anybodys
            if (lobby.LocalReady)
            {
                lobby.SetReady(false);
            }
            else
            {
                setup.Slots.Clear();
            }
            return true;
        }

        if (gamepad.WasPressed(GamepadInput.X) && !lobby.LocalReady)
        {
            // Who we play is ours to pick whenever we like, as long as we havent said we are ready
            Engine.SetScene(new CharacterSelectScene(setup));
            return true;
        }

        // Corners and being ready are between two players, there is nothing to settle while we are alone
        if (!lobby.PeerPresent) return false;

        if (GameInput.MenuLeftPressed(gamepad))
        {
            lobby.RequestSide(0);
            return true;
        }

        if (GameInput.MenuRightPressed(gamepad))
        {
            lobby.RequestSide(1);
            return true;
        }

        if (gamepad.WasPressed(GamepadInput.A))
        {
            if (!lobby.LocalReady)
            {
                lobby.SetReady(true);
            }
            else if (lobby.IsHost && lobby.BothReady)
            {
                // The map is the hosts to choose, the other player waits right here
                lobby.SetChoosingMap(true);
                Engine.SetScene(new MapSelectionScene(setup));
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Called for every gamepad nobody has picked yet, returns whether it did anything.
    /// </summary>
    private bool UpdateUnpicked(Gamepad gamepad)
    {
        if (setup.IsReady || gamepad.Pressed is not { } input) return false;

        // Any button picks the gamepad, apart from the one that means back everywhere else (and only while there is nothing to undo)
        if (input == GamepadInput.B && setup.Slots.Count == 0)
        {
            Back();
            return true;
        }

        setup.Slots.Add(gamepad.Slot);

        // The card of whoever just picked jumps, so there is no doubt it went to them
        cards[setup.Lobby?.LocalSide ?? setup.Slots.Count - 1].Box.Punch(0.1f, 0.3f);
        return true;
    }

    /// <summary>
    /// Called every update of an online fight's lobby, returns whether we have left the scene.
    /// </summary>
    private bool UpdateLobby(OnlineLobby lobby)
    {
        if (lobby.IsClosed)
        {
            // The host is gone and the lobby with them
            Back();
            return true;
        }

        if (lobby.StartedMap is { } mapFile)
        {
            // The host has chosen, follow them into the fight
            var mapDefinition = MapLoader.LoadDefinitions(Fighter2D.Content.GameContent.PathOf(Fighter2D.Content.GameContent.MAPS_FILE)).FirstOrDefault(map => map.FileName == mapFile);
            if (mapDefinition.FileName is null)
            {
                Console.WriteLine($"[Lobby] The host started on '{mapFile}', which is not a map we have.");
                Back();
                return true;
            }

            Engine.SetScene(setup.CreateFight(mapDefinition));
            return true;
        }

        // Everybody notices when somebody walks in (or out), and when the corners or who is ready change
        if (lobby.PeerPresent != lobbyHadPeer)
        {
            lobbyHadPeer = lobby.PeerPresent;
            cards[1 - lobby.LocalSide].Box.Punch(0.1f, 0.3f);
        }
        else if (lobby.Revision != lobbyRevision)
        {
            foreach (Card card in cards)
            {
                card.Box.Punch(0.05f, 0.25f);
            }
        }

        lobbyRevision = lobby.Revision;
        return false;
    }

    /// <summary>
    /// Helper method to take a gamepad away from its player when it is pulled out, along with everybody who picked after them.
    /// </summary>
    private void DropUnplugged()
    {
        for (int i = 0; i < setup.Slots.Count; i++)
        {
            if (GameInput.Manager.TryGet(setup.Slots[i], out Gamepad gamepad) && gamepad.IsConnected) continue;

            setup.Slots.RemoveRange(i, setup.Slots.Count - i);

            // Nobody can be ready without a gamepad
            setup.Lobby?.SetReady(false);
            return;
        }
    }

    private void UpdateCards()
    {
        for (int i = 0; i < cards.Length; i++)
        {
            Card card = cards[i];

            if (i < setup.Slots.Count && GameInput.Manager.TryGet(setup.Slots[i], out Gamepad gamepad))
            {
                // Holding anything turns the name white, which is how you check the gamepad in your hands is the one you picked
                card.Status.Text = Shorten(gamepad.Name);
                card.Status.Color = gamepad.AnyDown ? Vector4.One : NameColor;
                card.Detail.Text = GameInput.Localize("[icon:pad_y] bindings", gamepad);
            }
            else if (i == setup.Slots.Count)
            {
                ShowWaitingForButton(card);
            }
            else
            {
                card.Status.Text = "waiting";
                card.Status.Color = HintColor;
                card.Detail.Text = $"for player {i}";
            }
        }

        // The hint is for everybody, so it shows the buttons of whichever gamepad was touched last
        Gamepad? lastUsed = GameInput.Manager.LastUsed;

        if (setup.IsReady)
        {
            hint.Text = GameInput.Localize(HINT_READY, lastUsed);
        }
        else if (GameInput.Manager.ConnectedCount <= setup.Slots.Count)
        {
            hint.Text = "Plug in a gamepad to continue";
        }
        else
        {
            hint.Text = GameInput.Localize(setup.Slots.Count == 0 ? "[icon:pad_b] back" : "[icon:pad_y] bindings    [icon:pad_b] undo", lastUsed);
        }
    }

    /// <summary>
    /// Helper method to show the lobby of an online fight: the cards are the two corners, each with whoever is standing in it.
    /// </summary>
    private void UpdateLobbyCards(OnlineLobby lobby)
    {
        Gamepad? gamepad = null;
        if (setup.Slots.Count > 0) GameInput.Manager.TryGet(setup.Slots[0], out gamepad);

        Card ours = cards[lobby.LocalSide];
        Card theirs = cards[1 - lobby.LocalSide];

        if (gamepad is null)
        {
            ShowWaitingForButton(ours);
        }
        else
        {
            ours.Status.Text = $"you - {Shorten(gamepad.Name)}";
            ours.Status.Color = gamepad.AnyDown ? Vector4.One : NameColor;
            ShowReady(ours, lobby.LocalReady, lobby.PeerPresent ? GameInput.Localize(HINT_CORNER, gamepad) : string.Empty);

            // Nobody gets to be ready before the files of the fight are here
            if (!lobby.IsHost && lobby.Content.Phase != ContentPhase.Ready)
            {
                ours.Detail.Text = DescribeContent(lobby.Content);
                ours.Detail.Color = lobby.Content.Phase == ContentPhase.Failed ? ErrorColor : HintColor;
            }
        }

        if (lobby.PeerPresent)
        {
            theirs.Status.Text = "your opponent";
            theirs.Status.Color = Vector4.One;
            ShowReady(theirs, lobby.RemoteReady, "making up their mind");

            // The host sees how far along the other player is with getting its files
            if (lobby.IsHost && !lobby.Content.PeerVerified)
            {
                theirs.Detail.Text = lobby.Content.IsTransferring
                    ? $"sending them the game files {lobby.Content.Progress * 100:0}%"
                    : "checking their game files";
            }
        }
        else
        {
            // Only the host ever sees this, somebody who joined has the host for company
            float pulse = 0.65f + 0.35f * MathF.Sin(totalTime * 3.0f);

            theirs.Status.Text = "waiting for a player";
            theirs.Status.Color = new Vector4(pulse, pulse, pulse, 1.0f);
            theirs.Detail.Text = "to join";
            theirs.Detail.Color = HintColor;
        }

        if (!lobby.PeerPresent)
        {
            hint.Text = $"They can find you at {hostAddresses}";
        }
        else if (lobby.BothReady)
        {
            hint.Text = lobby.IsHost
                ? GameInput.Localize("[icon:pad_a] choose the map    [icon:pad_b] not ready", gamepad)
                : lobby.ChoosingMap ? "The host is choosing the map" : "Waiting for the host to choose the map";
        }
        else
        {
            hint.Text = GameInput.Localize("[icon:pad_x] fighter    [icon:pad_y] bindings    [icon:pad_b] back", gamepad);
        }
    }

    /// <summary>
    /// Helper method to say how the other player is doing with getting the files of the host.
    /// </summary>
    private static string DescribeContent(ContentSync content) => content.Phase switch
    {
        ContentPhase.Downloading => $"getting the game files {content.Progress * 100:0}%",
        ContentPhase.Failed => $"no fight: {content.Error}",
        _ => "checking the game files"
    };

    private void ShowWaitingForButton(Card card)
    {
        // The player whose turn it is gets a text that pulses
        float pulse = 0.65f + 0.35f * MathF.Sin(totalTime * 6.0f);

        card.Status.Text = TEXT_WAITING;
        card.Status.Color = new Vector4(pulse, pulse, pulse, 1.0f);
        card.Detail.Text = "on your gamepad";
        card.Detail.Color = HintColor;
    }

    private static void ShowReady(Card card, bool ready, string otherwise)
    {
        card.Detail.Text = ready ? "ready!" : otherwise;
        card.Detail.Color = ready ? ReadyColor : HintColor;
    }

    private void Back()
    {
        // Leaving the lobby is hanging up on whoever is in it
        setup.Close();
        Engine.SetScene(new MainMenuScene());
    }

    private void CompositeImages()
    {
        var spriteBatch = screen.AddEntity<SpriteBatch>();

        if (Engine.ObjectManager.Textures.TryCreateOrGet("gpselbg", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/backgrounds/player_select_bg.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_bg))
        {
            var bg = spriteBatch.AddEntity(new Sprite(Engine.WindowManager.WindowSize));
            bg.Transform.SetPositionRelativeToOrigin(new System.Numerics.Vector2(-Engine.WindowManager.WindowSize.X / 2, Engine.WindowManager.WindowSize.Y / 2));
            bg.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_bg.Asset, new Vector2(result_bg.Asset.Width, result_bg.Asset.Height)), "bg");

            spriteBatch.Add(bg);
        }
    }

    private void CompositeUi()
    {
        // The screen is laid out in Assets/ui/layouts/gamepad_select.hor, the cards in player_card.hor
        // How the panel pops up and the cards follow one after the other is in there as well
        UILayout layout = MenuLayouts.Load(this, (Camera2D)ActiveCamera, MenuLayouts.GAMEPAD_SELECT);

        panel = layout.Get<StackPanel>("panel");
        layout.Get<Label>("title").Text = setup.Title;

        // One card per player, side by side. Online there are always two of them, one for each corner of the fight
        var items = layout.Populate("cards", setup.IsOnline ? 2 : setup.PlayerCount);
        cards = new Card[items.Count];
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i] = CompositeCard(items[i], setup.IsOnline ? (i == 0 ? "Blue corner" : "Red corner") : $"Player {i + 1}");
        }

        // The skin draws the buttons of the gamepad where the text asks for them
        hint = layout.Get<Label>("hint");
        hint.Text = string.Empty;

        // For the mouse, a gamepad goes back with B
        layout.Get<Button>("btn_back").OnPressed = Back;
    }

    /// <summary>
    /// Helper method to pick the parts of a card out of the layout it was made from.
    /// </summary>
    private static Card CompositeCard(UILayout item, string title)
    {
        item.Get<Label>("title").Text = title;

        return new Card
        {
            Box = item.Get<StackPanel>("box"),
            Status = item.Get<Label>("status"),
            Detail = item.Get<Label>("detail")
        };
    }

    /// <summary>
    /// Helper method to cut a name short, some gamepads have a lot to say about themselves.
    /// </summary>
    private static string Shorten(string name)
    {
        if (name.Length == 0) return "Gamepad";

        return name.Length <= MAX_NAME_LENGTH ? name : name[..(MAX_NAME_LENGTH - 2)] + "..";
    }
}
