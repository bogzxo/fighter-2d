using System.Numerics;

using Horizon.Physics;
using Horizon.Physics.Fixtures;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Effects;

/// <summary>
/// One of the two fighters in the background of the main menu. Nobody plays it, the <see cref="MenuDuel"/> tells it which frame to show and where to stand.
/// It has a physics body shaped like whatever frame it is showing, which is what the sparks of the duel bounce off.
/// </summary>
internal sealed class MenuFighter(float size, string spriteDirectory) : Sprite(new Vector2(size))
{
    // How long (in seconds) the white flash of getting hit takes to fade
    private const float FLASH_TIME = 0.25f;

    // The colour of the corner this fighter is in
    public Vector4 BaseTint;

    public PhysicsBodyComponent2D Body = null!;

    private OutlinePhysicsFixture? _outline;
    private CharacterBoxes? _boxes;
    private (string? Animation, int Frame, bool Flipped) _shown;
    private Vector2[] _pieces = [];

    public override void Initialize()
    {
        base.Initialize();

        var (success, sheet, manager) = SpriteSheet.LoadSpriteSheetFromDirectory(spriteDirectory);

        Spritesheet = sheet;
        AnimationManager = manager;

        if (success) _boxes = CharacterBoxes.Load(spriteDirectory, manager);

        // The duel decides which frame is shown
        AnimationManager.AnimateFrames = false;
        SetAnimation("idle");
    }

    public void Show(string animation, int frame)
    {
        // The sheet is loaded on the render thread, which can be after the first update
        if (AnimationManager is null) return;

        SetAnimation(animation);
        AnimationManager.SetFrame(animation, (uint)frame);

        // Made here rather than along with the sheet because this is the thread the physics runs on
        _outline ??= Body.CreateOutlineFixture("outline");

        // The outline only needs handing over when what is drawn of us has changed
        if (_shown == (animation, frame, Flipped)) return;
        _shown = (animation, frame, Flipped);

        Vector2[] traced = _boxes is not null && _boxes.TryGet(animation, (uint)frame, out var found) ? found.Outline : [];
        if (_pieces.Length < traced.Length) _pieces = new Vector2[traced.Length];

        // At the size and the way round we are drawn
        Vector2 scale = new(Flipped ? -size : size, size);
        for (int i = 0; i < traced.Length; i++) _pieces[i] = traced[i] * scale;

        _outline.Set(_pieces.AsSpan(0, traced.Length));
    }

    /// <summary>
    /// Helper method to put the fighter somewhere, body and all. How fast it got there is what it shoves the sparks aside with.
    /// </summary>
    public void MoveTo(Vector2 position, float dt)
    {
        Body.SetVelocity(dt > 0.0f ? (position - Body.Position) / dt : Vector2.Zero);
        Body.Position = position;
        Transform.Position = position;

        // Without the time it took it was put there rather than moved, and isn't shown on its way
        if (dt <= 0.0f) Transform.Snap();
    }

    /// <summary>
    /// Flashes the fighter white and fades it back to its corner colour, for when it takes a hit.
    /// </summary>
    public void Flash()
    {
        Tint = Vector4.One;
        this.TweenTint(BaseTint, FLASH_TIME);
    }
}
