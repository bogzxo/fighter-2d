using System;
using System.Collections.Generic;
using System.Numerics;

using Fighter2D.Character;
using Fighter2D.Content;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.Scenes;

/// <summary>
/// A character in a menu doing one of the animations out of its sprite sheet, played inside of an image of the UI.
/// The image comes out of a layout file, this only decides what it shows.
/// </summary>
internal class CharacterPortrait(Image image)
{
    // The sprite sheets that have been loaded for portraits, one per character however many portraits show it
    private static readonly Dictionary<string, (SpriteSheet Sheet, SpriteSheetAnimationManager Animations)> sheets = [];

    private SpriteSheet? sheet;
    private Vector2 firstFrame;
    private uint length = 1;
    private float frameTime = 1.0f / 12.0f;
    private float timer;
    private uint frame;

    /// <summary>
    /// Helper method to show a character. Has to be called from the render thread the first time a character is shown, its sprite sheet is loaded then.
    /// </summary>
    /// <param name="animation">The animation to play, the idle one if the character doesn't have it.</param>
    public void Show(CharacterDefinition character, string animation = "idle")
    {
        string directory = GameContent.PathOf(character.SpriteDirectory);

        if (!sheets.TryGetValue(directory, out var loaded))
        {
            // Kept for every portrait that comes after, so it must not go with the scene that showed the character first
            using var nobody = Horizon.Content.AssetScope.EnterGlobal();

            var (success, loadedSheet, animations) = SpriteSheet.LoadSpriteSheetFromDirectory(directory);
            if (!success)
            {
                // A character without sprites still has a cell, there is just nothing in it
                image.Texture = null;
                sheet = null;
                return;
            }

            sheets[directory] = loaded = (loadedSheet, animations);
        }

        if (!loaded.Animations.Animations.TryGetValue(animation, out var definition)
            && !loaded.Animations.Animations.TryGetValue("idle", out definition))
        {
            return;
        }

        sheet = loaded.Sheet;
        image.Texture = sheet;

        // The definition counts in cells of the sheet, the frames of an animation follow each other to the right
        firstFrame = definition.FirstFrame.Position * sheet.SpriteSize;
        length = Math.Max(1, definition.Length);
        frameTime = 1.0f / character.FrameRate * 2.0f;
        frame = 0;
        timer = 0;

        ShowFrame();
    }

    /// <summary>
    /// Whether the character is shown facing left rather than the way its art was drawn, for whoever stands on the right.
    /// </summary>
    public bool Mirrored { get; init; }

    /// <summary>The image the character is shown in.</summary>
    public Image Image => image;

    /// <summary>
    /// Called every update by the scene the portrait is in, to move the animation along.
    /// </summary>
    public void Update(float dt)
    {
        if (sheet is null || length < 2) return;

        timer += dt;
        while (timer >= frameTime)
        {
            timer -= frameTime;
            frame = (frame + 1) % length;
        }

        ShowFrame();
    }

    private void ShowFrame()
    {
        if (sheet is null) return;

        Vector2 corner = firstFrame + new Vector2(sheet.SpriteSize.X * frame, 0);

        // Read from its right edge back to its left one, which is the same frame the other way round
        image.SourcePosition = Mirrored ? corner + new Vector2(sheet.SpriteSize.X, 0) : corner;
        image.SourceSize = Mirrored ? sheet.SpriteSize * new Vector2(-1, 1) : sheet.SpriteSize;
    }
}
