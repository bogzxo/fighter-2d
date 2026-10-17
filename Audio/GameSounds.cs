using System;
using System.IO;

using Horizon.Audio;
using Horizon.Engine;

namespace Fighter2D.Audio;

/// <summary>What a sound of the menus is for, see <see cref="GameSounds.Ui"/>.</summary>
internal enum UiSound
{
    Move,
    Pick,
    LockIn,
    Back,
    Refuse,
    Open,
    Close,
    Switch
}

/// <summary>
/// Every sound of the game, read the first time anything is played (out of Assets/sounds, whose README says where each
/// came from), and the few ways they're played. A hit has a handful of takes and each play picks one and nudges its pitch
/// a hair, the same punch twelve times over in a combo sounds like a machine. Music is streamed, one track at a time,
/// asking for the one that's playing already carries on with it, so going from the main menu to the setup screen doesn't
/// start it over. The volumes are the options' (<see cref="ApplyVolumes"/>). Everything here is from any thread and goes
/// to the engine's mixer, which is there whether or not there is a device.
/// </summary>
internal static class GameSounds
{
    private const string FOLDER = "Assets/sounds";

    // How much a take is pitched up or down at random, either way
    private const float PITCH_JITTER = 0.06f;

    // How loud the music sits under everything else, and the rain under the fight
    private const float MUSIC_LEVEL = 0.55f;
    private const float AMBIENCE_LEVEL = 0.35f;

    public const string MENU_MUSIC = "music/menu.ogg";
    public const string FIGHT_MUSIC = "music/fight.ogg";
    public const string RAIN = "ambience/rain.ogg";

    private static AudioMixer Mixer => GameEngine.Instance.Audio.Mixer;

    private static readonly Lazy<Library> library = new(() => new Library());
    private static Library Sounds => library.Value;

    private static string? playingMusic;
    private static SoundHandle ambience;
    private static string? playingAmbience;

    private sealed class Library
    {
        public readonly Sound?[] HitLight = Takes("hits/hit_light", 5), HitHeavy = Takes("hits/hit_heavy", 5);
        public readonly Sound?[] Block = Takes("hits/block", 3), Whiff = Takes("hits/whiff", 4), Step = Takes("hits/step", 5);
        public readonly Sound? Counter = One("hits/counter"), Parry = One("hits/parry"), Land = One("hits/land"), KnockOut = One("hits/knock_out");
        public readonly Sound?[] Ui = [One("ui/move"), One("ui/pick"), One("ui/lock_in"), One("ui/back"), One("ui/refuse"), One("ui/open"), One("ui/close"), One("ui/switch")];

        public Library()
        {
            // Footsteps of two fighters and the cursor clicking along a roster come thick and fast, a few at a time is plenty
            foreach (Sound? step in Step) if (step is not null) step.MaxPlaying = 2;
            if (Ui[(int)UiSound.Move] is { } move) move.MaxPlaying = 2;
        }
    }

    private static Sound? One(string name) => Sound.Load(GameContent.PathOf(Path.Combine(FOLDER, name + ".ogg")));

    private static Sound?[] Takes(string name, int count)
    {
        var takes = new Sound?[count];
        for (int i = 0; i < count; i++) takes[i] = One($"{name}_{i}");
        return takes;
    }

    private static void PlayTake(Sound?[] takes, float volume, float pan, float pitch = 1.0f) =>
        Mixer.Play(takes[Random.Shared.Next(takes.Length)], AudioBus.Effects, volume, pitch * Jitter(), pan);

    private static float Jitter() => 1.0f + (Random.Shared.NextSingle() * 2.0f - 1.0f) * PITCH_JITTER;

    /// <summary>The volumes of the options onto the mixer, at start and whenever they change.</summary>
    public static void ApplyVolumes()
    {
        AudioMixer mixer = Mixer;
        mixer.SetVolume(AudioBus.Music, GameOptions.MusicVolume);
        mixer.SetVolume(AudioBus.Effects, GameOptions.EffectsVolume);
        mixer.SetVolume(AudioBus.Interface, GameOptions.InterfaceVolume);
    }

    /* A fight */

    /// <summary>A hit that landed, heavier and lower the more it did.</summary>
    /// <param name="weight">From 0 for a jab to 1 for the biggest thing there is.</param>
    public static void Hit(float weight, float pan) =>
        PlayTake(weight > 0.5f ? Sounds.HitHeavy : Sounds.HitLight, 0.75f + 0.25f * weight, pan, 1.08f - 0.16f * Math.Clamp(weight, 0.0f, 1.0f));

    /// <summary>The ding over a counter hit or a punish, on top of the hit itself.</summary>
    public static void Counter(float pan) => Mixer.Play(Sounds.Counter, AudioBus.Effects, 0.5f, Jitter(), pan);

    public static void Block(float pan) => PlayTake(Sounds.Block, 0.8f, pan);

    public static void Parry(float pan) => Mixer.Play(Sounds.Parry, AudioBus.Effects, 0.9f, 1.0f, pan);

    public static void Whiff(float pan) => PlayTake(Sounds.Whiff, 0.45f, pan);

    public static void Step(float pan) => PlayTake(Sounds.Step, 0.3f, pan);

    public static void Land(float pan, float weight) => Mixer.Play(Sounds.Land, AudioBus.Effects, 0.35f + 0.4f * weight, Jitter(), pan);

    public static void KnockOut() => Mixer.Play(Sounds.KnockOut, AudioBus.Effects, 1.0f, 0.9f);

    /// <summary>The announcer, a line out of voice/ by its name (round_1, fight, flawless_victory...).</summary>
    public static void Call(string line) => Mixer.Play(One("voice/" + line), AudioBus.Effects, 0.9f);

    /* The menus */

    public static void Ui(UiSound sound) => Mixer.Play(Sounds.Ui[(int)sound], AudioBus.Interface, sound == UiSound.Move ? 0.5f : 0.8f);

    /* Music and what goes on under a fight */

    /// <summary>Plays a track out of Assets/sounds, or carries on with it if it's the one playing. Null fades it out.</summary>
    public static void Music(string? track, float fade = 1.0f)
    {
        if (track == playingMusic) return;
        playingMusic = track;

        if (track is null)
        {
            Mixer.StopMusic(fade);
            return;
        }

        string path = GameContent.PathOf(Path.Combine(FOLDER, track));
        if (!File.Exists(path))
        {
            Mixer.StopMusic(fade);
            return;
        }

        try
        {
            Mixer.PlayMusic(new OggStream(path), fade, MUSIC_LEVEL);
        }
        catch (Exception exception)
        {
            Horizon.Logging.Log.Warning($"[GameSounds] Couldn't play '{track}': {exception.Message}");
        }
    }

    /// <summary>A loop under everything (the rain of a map), null for quiet. The one that's playing carries on.</summary>
    public static void Ambience(string? loop, float fade = 1.0f)
    {
        if (loop == playingAmbience) return;
        playingAmbience = loop;

        Mixer.Stop(ambience, fade);
        ambience = loop is null ? default : Mixer.Play(One(loop[..^4]), AudioBus.Effects, AMBIENCE_LEVEL, 1.0f, 0.0f, loop: true);
    }
}
