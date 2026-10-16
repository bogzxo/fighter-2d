using System;

using Horizon.Rendering.Spriting;
using Horizon.UI.Components;

namespace Fighter2D.Scenes.Widgets;

/// <summary>
/// A character playing one of its animations inside of an image of the UI, for the menus and the versus screen.
/// The image comes out of a layout file, this only decides what it shows. The frames come out of the atlas of the
/// character (see <see cref="CharacterArt"/>), the same one the fight draws them out of.
/// </summary>
internal class CharacterPortrait(Image image)
{
    private const string FALLBACK_ANIMATION = "idle";

    // Portraits play at half the speed of the fight, they are for looking at
    private const float SLOWDOWN = 2.0f;

    // What the frames of the animation go by in the atlas
    private string[] _frames = [];
    private float _frameTime = 1.0f / 12.0f;
    private float _timer;
    private int _frame;

    /// <summary>
    /// Whether the character is shown facing left instead of the way its art was drawn, for whoever stands on the right.
    /// </summary>
    public bool Mirrored { get; init; }

    /// <summary>The image the character is shown in.</summary>
    public Image Image => image;

    /// <summary>
    /// Helper method to show a character.
    /// </summary>
    /// <param name="animation">The animation to play, the idle one if the character doesn't have it.</param>
    public void Show(CharacterDefinition character, string animation = FALLBACK_ANIMATION)
    {
        if (CharacterArt.Load(character) is not { } art || !art.TryFind(animation, out SpriteSource source))
        {
            // A character without sprites still gets a cell, there is just nothing in it
            image.Atlas = null;
            _frames = [];
            return;
        }

        // Asked for here, put in by the UI the next time it is drawn
        _frames = art.Atlas.Request(source);
        _frameTime = 1.0f / character.FrameRate * SLOWDOWN;
        _frame = 0;
        _timer = 0;

        image.Atlas = art.Atlas;
        image.Mirrored = Mirrored;

        ShowFrame();
    }

    /// <summary>
    /// Called every update by whoever owns the portrait, to move the animation along.
    /// </summary>
    public void Update(float dt)
    {
        if (_frames.Length < 2) return;

        _timer += dt;
        while (_timer >= _frameTime)
        {
            _timer -= _frameTime;
            _frame = (_frame + 1) % _frames.Length;
        }

        ShowFrame();
    }

    private void ShowFrame()
    {
        if (_frames.Length > 0) image.AtlasKey = _frames[Math.Min(_frame, _frames.Length - 1)];
    }
}
