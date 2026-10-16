using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.Logging;

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

    // The characters being got ready on a worker (see PrepareAhead) by their ids, so nobody starts one twice
    private static readonly Dictionary<string, Task> ahead = [];

    /// <summary>
    /// The animations of the character by name, every one of them a sprite with as many frames as the animation has.
    /// </summary>
    public SpriteSheetDefinition Sprites { get; }

    /// <summary>
    /// What the frames are drawn out of. It outlives whatever scene asked for it first, and keeps only the pixels of a
    /// frame that are anything, a mannequin in the middle of 128 by 128 of air is mostly air.
    /// </summary>
    public TextureAtlas Atlas { get; } = new(ATLAS_WIDTH, ATLAS_HEIGHT, shared: true) { Trim = true, PacksOnWorker = true };

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
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                read = new CharacterArt(SpriteSheetDefinition.Open(path));
                Log.Info($"[CharacterArt] Read '{character.Id}' in {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms.");
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

        lock (ahead) ahead.Clear();

        // The files themselves are read again as well, they are kept by the engine
        AsepriteDocument.Forget();
    }

    /// <summary>
    /// Gets a character ready for a fight on a worker, read, the animations of its moves asked for from the atlas and
    /// their boxes traced, for a screen that knows who is about to fight (the fight setup does it for whoever a cursor
    /// rests on). Whoever gets it ready for real then finds it done, or waits only for what's left. Once a character,
    /// any thread.
    /// </summary>
    public static void PrepareAhead(CharacterDefinition character)
    {
        lock (ahead)
        {
            if (ahead.ContainsKey(character.Id)) return;
            ahead[character.Id] = Task.Run(() => PrepareNow(character));
        }
    }

    private static void PrepareNow(CharacterDefinition character)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (Load(character) is not { } art) return;
            art.Prepare(character.LoadMoves().Animations);
            art.Atlas.Pack();
            Log.Info($"[CharacterArt] Got '{character.Id}' ready ahead in {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms, on a worker.");
        }
        catch (Exception exception)
        {
            // Not the end of the world, the fight gets it ready itself
            Log.Warning($"[CharacterArt] Couldn't get '{character.Id}' ready ahead: {exception.Message}");
        }
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
        double atlasMs = 0.0, tracingMs = 0.0;
        int frames = 0;
        foreach (string animation in animations)
        {
            if (!Sprites.TryGetSprite(animation, null, out SpriteSource source)) continue;

            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Atlas.Request(source);
            long traced = System.Diagnostics.Stopwatch.GetTimestamp();
            Boxes.Trace(animation);
            atlasMs += System.Diagnostics.Stopwatch.GetElapsedTime(started, traced).TotalMilliseconds;
            tracingMs += System.Diagnostics.Stopwatch.GetElapsedTime(traced).TotalMilliseconds;
            frames += Math.Max(1, source.Frames);
        }

        // Worth knowing about when it is slow, this is a good share of what a fight costs to set up
        if (atlasMs + tracingMs > 20.0)
            Log.Info($"[CharacterArt] {frames} frames of '{Sprites.Path}' got ready in {atlasMs + tracingMs:0} ms, {atlasMs:0} of them going in the atlas and {tracingMs:0} being traced for boxes.");
    }
}
