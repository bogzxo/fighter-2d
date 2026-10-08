using System;
using System.Collections.Generic;
using System.Numerics;

using Bogz.Logging;

using Horizon.Rendering.Spriting;

namespace Fighter2D.Fighters;

/// <summary>
/// The art of a character. That is where its animations are (out of its .ase file, or an old sheet with a definition.hor),
/// the atlas its frames are drawn out of and the boxes that were traced off them.
/// Everybody who shows the character shares the one of these (both players of a mirror match, the portraits, the menu),
/// so a character is only ever read and traced once however many of them are on screen.
/// </summary>
internal sealed class CharacterArt
{
    // Wide enough that a whole character fits without the atlas growing past what a graphics card takes
    private const int ATLAS_WIDTH = 2048;
    private const int ATLAS_HEIGHT = 1024;

    private const string FALLBACK_ANIMATION = "idle";

    // By where the art is on disk. A character that couldn't be read is remembered as null so nobody keeps trying
    private static readonly Dictionary<string, CharacterArt?> cache = [];

    /// <summary>
    /// The animations of the character by name, every one of them a sprite with as many frames as the animation has.
    /// </summary>
    public SpriteSheetDefinition Sprites { get; }

    /// <summary>
    /// What the frames are drawn out of. It outlives whatever scene asked for it first.
    /// </summary>
    public TextureAtlas Atlas { get; } = new(ATLAS_WIDTH, ATLAS_HEIGHT, shared: true);

    /// <summary>
    /// The hurtbox, hitbox and outline of every frame, see <see cref="CharacterBoxes"/>.
    /// </summary>
    public CharacterBoxes Boxes { get; }

    /// <summary>
    /// How big a frame of the character is in pixels of its art.
    /// </summary>
    public Vector2 FrameSize { get; }

    private CharacterArt(SpriteSheetDefinition sprites)
    {
        Sprites = sprites;
        Boxes = new CharacterBoxes(sprites);

        if (TryFind(FALLBACK_ANIMATION, out SpriteSource idle)) FrameSize = new Vector2(idle.Width, idle.Height);
    }

    /// <summary>
    /// Helper method to get the art of a character, read the first time anybody asks. Null if it can't be read, which has been logged.
    /// </summary>
    public static CharacterArt? Load(CharacterDefinition character)
    {
        string path = GameContent.PathOf(character.Sprites);

        lock (cache)
        {
            if (cache.TryGetValue(path, out var known)) return known;

            CharacterArt? read = null;
            try
            {
                read = new CharacterArt(SpriteSheetDefinition.Open(path));
            }
            catch (Exception exception)
            {
                Log.Error($"Couldn't read the sprites of '{character.Id}' ({path}): {exception.Message}");
            }

            return cache[path] = read;
        }
    }

    /// <summary>
    /// Forgets every character that was read, for when the art on disk has changed. Whoever shows one next reads it again.
    /// Sprites that are still showing an old one keep showing it until they are gone.
    /// </summary>
    public static void Forget()
    {
        lock (cache)
        {
            foreach (CharacterArt? art in cache.Values) art?.Atlas.Dispose();
            cache.Clear();
        }

        // The files themselves are read again as well, they are kept by the engine
        AsepriteDocument.Forget();
    }

    public bool Has(string animation) => Sprites.Has(animation);

    /// <summary>
    /// How many frames an animation has, 0 for one the character doesn't have.
    /// </summary>
    public int FrameCount(string? animation) => animation is null ? 0 : Sprites.FrameCount(animation);

    /// <summary>
    /// Helper method to find an animation, the idle one for a name the character doesn't have.
    /// </summary>
    public bool TryFind(string animation, out SpriteSource source) =>
        Sprites.TryGetSprite(animation, null, out source) || Sprites.TryGetSprite(FALLBACK_ANIMATION, null, out source);

    /// <summary>
    /// Gets animations ready to be shown: their frames are asked for from the atlas and their boxes are traced.
    /// Anything that isn't got ready still works, but turns up a frame late the first time it is shown, which is no good in the middle of a fight.
    /// </summary>
    public void Prepare(IEnumerable<string> animations)
    {
        foreach (string animation in animations)
        {
            if (!Sprites.TryGetSprite(animation, null, out SpriteSource source)) continue;

            Atlas.Request(source);
            Boxes.Trace(animation);
        }
    }
}
