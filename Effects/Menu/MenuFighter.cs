using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.Physics;
using Horizon.Physics.Fixtures;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Effects;

/// <summary>
/// One of the two fighters in the background of the main menu. Nobody plays it, the <see cref="MenuDuel"/> tells it which frame to show and where to stand.
/// It has a physics body shaped like whatever frame it is showing, which is what the sparks of the duel bounce off.
/// </summary>
internal sealed class MenuFighter(float size, CharacterDefinition character, IEnumerable<string> animations) : Sprite(new Vector2(size))
{
    // How long (in seconds) the white flash of getting hit takes to fade
    private const float FLASH_TIME = 0.25f;

    private const string IDLE = "idle";

    // The colour of the corner this fighter is in
    public Vector4 BaseTint;

    public PhysicsBodyComponent2D Body = null!;

    private OutlinePhysicsFixture? _outline;
    private CharacterArt? _art;
    private (string? Animation, int Frame, bool Flipped) _shown;
    private Vector2[] _pieces = [];

    public override void Initialize()
    {
        base.Initialize();

        // The duel decides which frame is shown
        Animated = false;

        _art = CharacterArt.Load(character);
        if (_art is null) return;

        // Everything the duel is going to show is in the atlas before it starts
        _art.Prepare(animations);
        ConfigureAtlas(_art.Atlas, _art.Sprites, IDLE);

        // Art that isn't drawn at a whole multiple of itself would come out with uneven pixels otherwise
        Smooth = true;
    }

    /// <summary>
    /// How many frames an animation of the fighter has, 1 for one it doesn't have.
    /// </summary>
    public int Length(string animation) => Math.Max(1, _art?.FrameCount(animation) ?? 1);

    /// <summary>
    /// Shows a frame of an animation.
    /// </summary>
    /// <param name="progress">How far into the animation, from 0 for its first frame to 1 for its last.</param>
    public void ShowAt(string animation, float progress) =>
        Show(animation, (int)(Math.Clamp(progress, 0.0f, 1.0f) * (Length(animation) - 1)));

    public void Show(string animation, int frame)
    {
        // The art is loaded on the render thread, which can be after the first update
        if (_art is null || Atlas is null || !SetAnimation(animation)) return;

        Frame = frame;

        // Made here rather than along with the art because this is the thread the physics runs on
        _outline ??= Body.CreateOutlineFixture("outline");

        // The outline only needs handing over when what is drawn of us has changed
        if (_shown == (animation, Frame, Flipped)) return;
        _shown = (animation, Frame, Flipped);

        Vector2[] traced = _art.Boxes.TryGet(animation, (uint)Frame, out var found) ? found.Outline : [];
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
