using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.Content;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.Scenes.Widgets;

/// <summary>
/// A character playing one of its animations inside of an image of the UI, for the menus and the versus screen.
/// The image comes out of a layout file, this only decides what it shows.
/// </summary>
internal class CharacterPortrait(Image image)
{
    private const string FALLBACK_ANIMATION = "idle";

    // Portraits play at half the speed of the fight, they are for looking at
    private const float SLOWDOWN = 2.0f;

    // The sprite sheets that have been loaded for portraits, one per character however many portraits show it
    private static readonly Dictionary<string, (SpriteSheet Sheet, SpriteSheetAnimationManager Animations)> sheets = [];

    /// <summary>
    /// Forgets every sheet that was loaded for a portrait, for when the art on disk has changed.
    /// </summary>
    public static void Forget() => sheets.Clear();

    private SpriteSheet? _sheet;
    private Vector2 _firstFrame;
    private uint _length = 1;
    private float _frameTime = 1.0f / 12.0f;
    private float _timer;
    private uint _frame;

    /// <summary>
    /// Whether the character is shown facing left instead of the way its art was drawn, for whoever stands on the right.
    /// </summary>
    public bool Mirrored { get; init; }

    /// <summary>The image the character is shown in.</summary>
    public Image Image => image;

    /// <summary>
    /// Helper method to show a character. Has to be called from the render thread the first time a character is shown, its sprite sheet is loaded then.
    /// </summary>
    /// <param name="animation">The animation to play, the idle one if the character doesn't have it.</param>
    public void Show(CharacterDefinition character, string animation = FALLBACK_ANIMATION)
    {
        if (!TryLoadSheet(character, out var loaded))
        {
            // A character without sprites still gets a cell, there is just nothing in it
            image.Texture = null;
            _sheet = null;
            return;
        }

        if (!loaded.Animations.Animations.TryGetValue(animation, out var definition)
            && !loaded.Animations.Animations.TryGetValue(FALLBACK_ANIMATION, out definition))
        {
            return;
        }

        _sheet = loaded.Sheet;
        image.Texture = _sheet;

        // The definition counts in cells of the sheet, the frames of an animation follow each other to the right
        _firstFrame = definition.FirstFrame.Position * _sheet.SpriteSize;
        _length = Math.Max(1, definition.Length);
        _frameTime = 1.0f / character.FrameRate * SLOWDOWN;
        _frame = 0;
        _timer = 0;

        ShowFrame();
    }

    private static bool TryLoadSheet(CharacterDefinition character, out (SpriteSheet Sheet, SpriteSheetAnimationManager Animations) loaded)
    {
        string directory = GameContent.PathOf(character.SpriteDirectory);
        if (sheets.TryGetValue(directory, out loaded)) return true;

        // Kept for every portrait that comes after, so it must not get freed along with the scene that showed the character first
        using var nobody = AssetScope.EnterGlobal();

        var (success, sheet, animations) = SpriteSheet.LoadSpriteSheetFromDirectory(directory);
        if (!success) return false;

        sheets[directory] = loaded = (sheet, animations);
        return true;
    }

    /// <summary>
    /// Called every update by whoever owns the portrait, to move the animation along.
    /// </summary>
    public void Update(float dt)
    {
        if (_sheet is null || _length < 2) return;

        _timer += dt;
        while (_timer >= _frameTime)
        {
            _timer -= _frameTime;
            _frame = (_frame + 1) % _length;
        }

        ShowFrame();
    }

    private void ShowFrame()
    {
        if (_sheet is null) return;

        Vector2 corner = _firstFrame + new Vector2(_sheet.SpriteSize.X * _frame, 0);

        // Mirrored is the same frame read from its right edge back to its left one
        image.SourcePosition = Mirrored ? corner + new Vector2(_sheet.SpriteSize.X, 0) : corner;
        image.SourceSize = Mirrored ? _sheet.SpriteSize * new Vector2(-1, 1) : _sheet.SpriteSize;
    }
}
