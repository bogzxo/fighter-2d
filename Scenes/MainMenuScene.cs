using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Input2;
using Horizon.Rendering;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Particles.Simulation;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// The first scene of the game, where the kind of fight is picked (or the options, once there are any).
/// Behind the menu two fighters go at each other forever: they run in, one kicks, the other blocks, and both slide back out in a shower of sparks.
/// </summary>
internal class MainMenuScene : Scene
{
    private const float INPUT_DELAY = 0.25f;
    private const string HINT = "[icon:dpad] choose    [icon:pad_a] pick";

    // The duel in the background, everything is in the units of the camera (pixels of the window)
    private const float FIGHTER_SIZE = 448;
    private const float DUEL_CENTER_X = 330;
    private const float DUEL_GROUND_Y = -330;
    private const float DUEL_GAP_FAR = 620;
    private const float DUEL_GAP_NEAR = 170;

    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);
    private static readonly Vector4 TintBlue = new(0.45f, 0.7f, 1.0f, 1.0f);
    private static readonly Vector4 TintRed = new(1.0f, 0.5f, 0.5f, 1.0f);

    /// <summary>
    /// The steps of the duel, in the order they follow each other.
    /// </summary>
    private enum DuelPhase
    {
        Approach,
        Strike,
        Recoil,
        Rest
    }

    // How long every step of the duel takes, in seconds
    private static readonly float[] PhaseDurations = [0.85f, 0.55f, 0.8f, 1.1f];

    /// <summary>
    /// A fighter of the background, the same sprite sheet the players use but shown frame by frame by the scene.
    /// </summary>
    private sealed class MenuFighter() : Sprite(new Vector2(FIGHTER_SIZE))
    {
        public Vector4 BaseTint;

        // How much of the flash of a hit is left, 1 right after it
        public float Flash;

        public override void Initialize()
        {
            base.Initialize();

            var (_, sheet, manager) = SpriteSheet.LoadSpriteSheetFromDirectory("Assets/sprites/player");

            this.Spritesheet = sheet;
            this.AnimationManager = manager;

            // The scene decides which frame is shown
            AnimationManager.AnimateFrames = false;
            SetAnimation("idle");
        }

        public void Show(string animation, int frame)
        {
            // The sheet is loaded on the render thread, which can be after the first update
            if (AnimationManager is null) return;

            SetAnimation(animation);
            AnimationManager.SetFrame(animation, (uint)frame);
        }
    }

    public override Camera ActiveCamera { get; protected set; } = null!;
    private Camera2D camera = null!;

    // Background
    private SpriteBatch spriteBatch = null!;
    private Sprite logo = null!;
    private Vector2 logoPosition;
    private MenuFighter fighterLeft = null!, fighterRight = null!;
    private ParticleRenderer2D particlesDust, sparksBlue, sparksRed;

    private DuelPhase duelPhase = DuelPhase.Rest;
    private float phaseTime = 0;
    private bool leftAttacks = false;
    private bool struck = false;

    // How hard the logo is still shaking from the last hit
    private float shake = 0;

    // UI Elements
    private readonly List<Button> buttons = [];
    private Label hint;

    // Whether the hint is busy saying something else than which buttons to press
    private bool hintIsMessage;
    private int selectedIndex = 0;
    private float _delayTimer = 0;

    public override void Initialize()
    {
        Engine.GL.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        Engine.GL.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);

        // Other scenes leave their own clear colour behind
        Engine.GL.ClearColor(System.Drawing.Color.Black);

        ActiveCamera = camera = AddEntity<Camera2D>(new(new System.Numerics.Vector2(Engine.WindowManager.WindowSize.X, Engine.WindowManager.WindowSize.Y)));

        CompositeBackground();
        CompositeUi();

        Select(0);

        base.Initialize();
    }

    private void CompositeBackground()
    {
        spriteBatch = AddEntity<SpriteBatch>();

        // The fighter on the left is the blue corner, the one on the right the red one
        fighterLeft = AddEntity(new MenuFighter { BaseTint = TintBlue, Tint = TintBlue });
        fighterRight = AddEntity(new MenuFighter { BaseTint = TintRed, Tint = TintRed });
        fighterRight.Flipped = true;

        spriteBatch.Add(fighterLeft);
        spriteBatch.Add(fighterRight);

        if (Engine.ObjectManager.Textures.TryCreateOrGet("main_logo", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/ui/logo.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_logo))
        {
            const uint logoScalar = 3;

            logo = spriteBatch.AddEntity(new Sprite(new System.Numerics.Vector2(result_logo.Asset.Width * logoScalar, result_logo.Asset.Height * logoScalar)));
            logo.Transform.Origin = Origin.TopLeft;
            logo.Transform.Position = logoPosition = new Vector2(Engine.WindowManager.WindowSize.X / -2, Engine.WindowManager.WindowSize.Y / 2);
            logo.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_logo.Asset, new System.Numerics.Vector2(result_logo.Asset.Width, result_logo.Asset.Height)), "logo");

            spriteBatch.Add(logo);

            // The logo grows out of its corner
            logo.PopIn(0.7f, 0.15f);
        }

        // Whatever the duel throws up is drawn in front of the fighters
        particlesDust = AddEntity(new ParticleRenderer2D(8192)
        {
            ParticleSize = 5,
            MaxAge = 0.7f,
            StartColor = new Vector3(0.55f, 0.5f, 0.45f),
            EndColor = new Vector3(0.08f, 0.07f, 0.06f),
            Gravity = new Vector2(0, 160)
        });
        sparksBlue = AddEntity(new ParticleRenderer2D(16384)
        {
            ParticleSize = 4,
            MaxAge = 1.1f,
            StartColor = new Vector3(0.7f, 0.95f, 1.0f),
            EndColor = new Vector3(0.0f, 0.2f, 0.9f),
            Gravity = new Vector2(0, -900)
        });
        sparksRed = AddEntity(new ParticleRenderer2D(16384)
        {
            ParticleSize = 4,
            MaxAge = 1.1f,
            StartColor = new Vector3(1.0f, 0.95f, 0.7f),
            EndColor = new Vector3(0.9f, 0.1f, 0.0f),
            Gravity = new Vector2(0, -900)
        });
    }

    private void CompositeUi()
    {
        var compositor = AddComponent(new UICompositor(camera, Constants.UI_THEME));

        // The menu sits on the left, the right of the screen belongs to the duel
        // It needs a panel of its own behind it, text is not readable on top of the fire
        var menu = compositor.CreateModule().AddComponent(new StackPanel
        {
            Anchor = Origin.Left,
            Position = new Vector2(90, -110),
            Background = "panel",
            Padding = new UIEdges(36, 32),
            Spacing = 16
        });

        AddButton(menu, "PVP", () => Play(MatchMode.Pvp));
        AddButton(menu, "Practice", () => Play(MatchMode.Practice));
        AddButton(menu, "Options", OpenOptions);

        // The skin draws the buttons of the gamepad where the text asks for them
        hint = menu.Add(new Label(HINT)
        {
            Size = new Vector2(0, 30),
            TextScale = 0.25f,
            Color = HintColor
        });

        menu.Add(new Label(Constants.VERSION_LABEL)
        {
            TextScale = 0.2f,
            Color = HintColor
        });

        // The menu comes in from the side, and its buttons pop up one after the other once it is there
        menu.SlideIn(new Vector2(-520, 0), 0.55f, 0.2f).SetEasing(Easing.OutBack);
        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].PopIn(0.35f, 0.55f + i * 0.1f);
        }
    }

    private void AddButton(StackPanel menu, string label, Action pressed)
    {
        buttons.Add(menu.Add(new Button(label) { Size = new Vector2(360, 0), OnPressed = pressed }));
    }

    private void Select(int index)
    {
        selectedIndex = Math.Clamp(index, 0, buttons.Count - 1);

        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].Selected = i == selectedIndex;
        }
    }

    private void Play(MatchMode mode)
    {
        Engine.SetScene(new GamepadSelectorScene(new MatchSetup(mode)));
    }

    private void OpenOptions()
    {
        // @bogz the options screen goes here, Engine.SetScene(new OptionsScene()) once there is one
        hintIsMessage = true;
        hint.Text = "options are on their way";
        hint.Shake(6.0f);
    }

    public override void UpdateState(float dt)
    {
        _delayTimer += dt;

        UpdateDuel(dt);
        base.UpdateState(dt);

        // The hint shows the buttons of whichever gamepad was touched last
        if (!hintIsMessage)
        {
            hint.Text = GameInput.Localize(HINT, GameInput.Manager.LastUsed);
        }

        // The button that got us here is most likely still held
        if (_delayTimer < INPUT_DELAY) return;

        // Nobody has picked a gamepad yet, so the menu listens to all of them
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            if (GameInput.MenuDownPressed(gamepad))
            {
                Select(selectedIndex + 1);
            }
            else if (GameInput.MenuUpPressed(gamepad))
            {
                Select(selectedIndex - 1);
            }
            else if (gamepad.WasPressed(GamepadInput.A) || gamepad.WasPressed(GamepadInput.Start))
            {
                buttons[selectedIndex].OnPressed?.Invoke();
                return;
            }
        }
    }

    private void UpdateDuel(float dt)
    {
        phaseTime += dt;

        float duration = PhaseDurations[(int)duelPhase];
        if (phaseTime >= duration)
        {
            phaseTime -= duration;
            duelPhase = (DuelPhase)(((int)duelPhase + 1) % PhaseDurations.Length);

            if (duelPhase == DuelPhase.Approach)
            {
                // They take turns, nobody likes a one sided fight
                leftAttacks = !leftAttacks;
                struck = false;
            }
        }

        // How far through the current step we are, from 0 to 1
        float t = phaseTime / PhaseDurations[(int)duelPhase];
        float gap = DUEL_GAP_FAR;

        MenuFighter attacker = leftAttacks ? fighterLeft : fighterRight;
        MenuFighter defender = leftAttacks ? fighterRight : fighterLeft;

        switch (duelPhase)
        {
            case DuelPhase.Approach:
                // Slow at both ends of the run
                gap = float.Lerp(DUEL_GAP_FAR, DUEL_GAP_NEAR, t * t * (3 - 2 * t));

                int stride = (int)(phaseTime * 16) % 12;
                attacker.Show("run_loop", stride);
                defender.Show("run_loop", (stride + 6) % 12);

                KickUpDust(1);
                break;

            case DuelPhase.Strike:
                gap = DUEL_GAP_NEAR;

                attacker.Show("kick", Math.Min(4, (int)(t * 6)));
                defender.Show("block", Math.Min(6, (int)(t * 9)));

                if (!struck && t > 0.35f)
                {
                    struck = true;
                    Strike(defender);
                }
                break;

            case DuelPhase.Recoil:
                // Fast at first, the way something slides to a halt
                gap = float.Lerp(DUEL_GAP_NEAR, DUEL_GAP_FAR, 1 - (1 - t) * (1 - t));

                attacker.Show("run_stop", Math.Min(4, (int)(t * 5)));
                defender.Show("run_stop", Math.Min(4, (int)(t * 5)));

                if (t < 0.6f) KickUpDust(2);
                break;

            default:
                attacker.Show("idle", 0);
                defender.Show("idle", 0);
                break;
        }

        float y = DUEL_GROUND_Y + FIGHTER_SIZE / 2;
        fighterLeft.Transform.Position = new Vector2(DUEL_CENTER_X - gap / 2, y);
        fighterRight.Transform.Position = new Vector2(DUEL_CENTER_X + gap / 2, y);

        FadeFlash(fighterLeft, dt);
        FadeFlash(fighterRight, dt);

        // The logo takes the hit as well
        shake = MathF.Max(0, shake - dt * 3.0f);
        if (logo is not null)
        {
            logo.Transform.Position = logoPosition + new Vector2(Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f) * shake * 14.0f;
        }
    }

    /// <summary>
    /// Helper method for the moment the kick lands on the block, sparks in the colour of whoever threw it.
    /// </summary>
    private void Strike(MenuFighter defender)
    {
        var sparks = leftAttacks ? sparksBlue : sparksRed;
        var impact = new Vector2(DUEL_CENTER_X, DUEL_GROUND_Y + FIGHTER_SIZE * 0.42f);

        // Most of it sprays past the one who blocked, the rest goes everywhere
        sparks.AddCone(impact, new Vector2(leftAttacks ? 1 : -1, 0.5f), MathF.PI / 1.5f, 500, 620);
        sparks.AddBurst(impact, 250, 380);

        defender.Flash = 1.0f;
        shake = 1.0f;
    }

    /// <summary>
    /// Helper method to throw dust out from under the feet of both fighters, away from the middle.
    /// </summary>
    private void KickUpDust(int count)
    {
        float feet = DUEL_GROUND_Y + 14;

        particlesDust.AddCone(new Vector2(fighterLeft.Transform.Position.X, feet), new Vector2(-1, 0.6f), MathF.PI / 3.0f, count, 140 + 120 * Random.Shared.NextSingle());
        particlesDust.AddCone(new Vector2(fighterRight.Transform.Position.X, feet), new Vector2(1, 0.6f), MathF.PI / 3.0f, count, 140 + 120 * Random.Shared.NextSingle());
    }

    private static void FadeFlash(MenuFighter fighter, float dt)
    {
        fighter.Flash = MathF.Max(0, fighter.Flash - dt * 4.0f);
        fighter.Tint = Vector4.Lerp(fighter.BaseTint, Vector4.One, fighter.Flash);
    }
}
