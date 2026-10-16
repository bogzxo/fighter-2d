using System.Numerics;

using Horizon.Engine;
using Horizon.Graphics;
using Horizon.Input;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.Menus;

/// <summary>
/// Base class for every menu of the game. It sets up the camera, the CRT renderer, the backdrop and the layout, which every menu used to copy paste.
/// A menu says which layout file it wants, wires up its parts in <see cref="BuildUi"/> and reads the gamepads in <see cref="UpdateMenu"/>.
/// </summary>
internal abstract class MenuScene : Scene
{
    private const string BACKDROP_FILE = "Assets/backgrounds/player_select_bg.png";

    public override Camera ActiveCamera { get; protected set; } = null!;
    protected Camera2D Camera { get; private set; } = null!;

    /// <summary>
    /// The renderer everything behind the UI is drawn into, it has the CRT effect on it.
    /// </summary>
    protected Renderer2D Canvas { get; private set; } = null!;

    /// <summary>
    /// The layout file of the menu, one of the constants of <see cref="MenuLayouts"/>.
    /// </summary>
    protected abstract string LayoutFile { get; }

    protected virtual Vector2 CameraSize => Engine.WindowManager.WindowSize;
    protected virtual System.Drawing.Color ClearColor => System.Drawing.Color.PaleVioletRed;

    /// <summary>
    /// How long (in seconds) the menu ignores the gamepads after it shows up. The button that got us here is most likely still held.
    /// </summary>
    protected virtual float InputDelay => 0.25f;
    protected bool InputReady => Time >= InputDelay;

    public override void Initialize()
    {
        ActiveCamera = Camera = AddEntity(new Camera2D(CameraSize));
        Engine.Graphics.ClearColor = new Vector4(ClearColor.R, ClearColor.G, ClearColor.B, ClearColor.A) / 255.0f;

        Canvas = Screen.CreateRenderer(this);

        Prepare();
        BuildBackdrop();

        UILayout layout = MenuLayouts.Load(this, Camera, LayoutFile);
        _ui = layout.Module.Compositor;
        _module = layout.Module;
        if (layout.TryGet<Label>("hint", out Label? hintLabel)) Hint = new HintBar(hintLabel, Actions);
        BuildUi(layout);
        BuildActions();

        base.Initialize();
    }

    // How far (in pixels) a menu is blurred while another screen is on top of it, and how long it takes to get
    // there and back. The same as a fight behind its pause menu, see FightScene
    private const float UNDERNEATH_BLUR = 10.0f;
    private const float UNDERNEATH_BLUR_TIME = 0.16f;

    private UICompositor _ui = null!;
    private UIModule _module = null!;

    /// <summary>
    /// What the buttons of a gamepad do on this screen, in the order they are tried in. A screen fills it in
    /// <see cref="BuildActions"/>, the base reads the gamepads and the hint writes itself out of it.
    /// </summary>
    protected MenuActions Actions { get; } = new();

    /// <summary>
    /// The line under the screen that says which buttons do what, null for a layout without a "hint" label.
    /// </summary>
    protected HintBar? Hint { get; private set; }

    /// <summary>
    /// Another screen was put on top of this one (the options over the main menu). What is behind the buttons
    /// blurs, the way a fight does behind its pause menu, and the buttons themselves go, the screen on top has its
    /// own to read.
    /// </summary>
    protected override void OnSuspended()
    {
        if (Canvas.PostProcessing.Get<Horizon.Rendering.PostProcessing.BlurEffect>() is { } blur)
        {
            blur.Enabled = true;
            blur.BlurTo(UNDERNEATH_BLUR, UNDERNEATH_BLUR_TIME);
        }

        _ui.Enabled = false;
    }

    /// <summary>Back on top, in focus again and with its buttons.</summary>
    protected override void OnResumed()
    {
        Canvas.PostProcessing.Get<Horizon.Rendering.PostProcessing.BlurEffect>()?.BlurTo(0.0f, UNDERNEATH_BLUR_TIME);
        Screen.ApplyOptions(Canvas, _ui);
        _ui.Enabled = true;
    }

    /// <summary>
    /// Called before anything is built, for menus that have to load something first.
    /// </summary>
    protected virtual void Prepare() { }

    /// <summary>
    /// Called to fill the screen behind the UI. Most menus share the same picture.
    /// </summary>
    protected virtual void BuildBackdrop()
    {
        if (!TryLoadTexture(BACKDROP_FILE, out Texture texture)) return;

        Vector2 window = Engine.WindowManager.WindowSize;
        var batch = Canvas.AddEntity<SpriteBatch>();

        var backdrop = batch.AddEntity(new Sprite(window));
        backdrop.Transform.SetPositionRelativeToOrigin(new Vector2(-window.X / 2, window.Y / 2));
        backdrop.ConfigureSpriteSheet(SpriteSheet.FromTexture(texture, new Vector2(texture.Width, texture.Height)), "bg");

        batch.Add(backdrop);
    }

    /// <summary>
    /// Called with the loaded layout, this is where a menu finds its parts by name and says what its buttons do.
    /// </summary>
    protected abstract void BuildUi(UILayout layout);

    /// <summary>
    /// Called after the layout is wired, this is where a menu says what the buttons of a gamepad do (see <see cref="Actions"/>).
    /// </summary>
    protected virtual void BuildActions() { }

    /// <summary>
    /// Called every update until the menu leaves for another scene, for what a menu shows. The gamepads are read after it, see <see cref="Actions"/>.
    /// </summary>
    protected abstract void UpdateMenu(float dt);

    public sealed override void UpdateState(float dt)
    {
        base.UpdateState(dt);

        // A menu that has left for another scene (see GoTo) stays on screen until the engine has covered it up, and stays quiet for as long as that takes.
        // So does one that is underneath another scene (the main menu behind the options), it keeps moving and keeps its hands off the gamepads
        if (IsLeaving || IsSuspended) return;

        UpdateMenu(dt);
        if (IsLeaving) return;

        Hint?.Update(dt);
        if (!InputReady) return;

        // A question that is up takes every gamepad, the rest of the screen waits for the answer
        if (_module.Dialog is { } dialog)
        {
            MenuActions.DriveDialog(dialog, _module.Navigation);
            return;
        }

        Actions.Update();
    }

    /// <summary>
    /// Helper method to load a picture, false if the file is fucked or missing.
    /// </summary>
    protected static bool TryLoadTexture(string file, out Texture texture)
    {
        texture = Texture.Load(file);
        return texture.IsValid;
    }
}
