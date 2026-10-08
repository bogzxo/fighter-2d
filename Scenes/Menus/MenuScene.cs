using System.Numerics;

using Horizon.Engine;
using Horizon.Graphics;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.UI;

namespace Fighter2D.Scenes.Menus;

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
        BuildUi(MenuLayouts.Load(this, Camera, LayoutFile));

        base.Initialize();
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
    /// Called every update until the menu leaves for another scene.
    /// </summary>
    protected abstract void UpdateMenu(float dt);

    public sealed override void UpdateState(float dt)
    {
        base.UpdateState(dt);

        // A menu that has left for another scene (see GoTo) stays on screen until the engine has covered it up, and stays quiet for as long as that takes
        if (!IsLeaving) UpdateMenu(dt);
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
