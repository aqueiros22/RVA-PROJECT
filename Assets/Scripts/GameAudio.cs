using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// Sound effects and music, picked by file name from Assets/Resources/Audio (see the README there).
// "hit_1", "hit_2"... are variants of "hit": one is picked at random. A missing sound is silent.
public class GameAudio : MonoBehaviour
{
    [Tooltip("Music volume in the menus.")]
    [Range(0f, 1f)] public float musicVolume = 0.35f;
    [Tooltip("Music volume in the duel, lower so the prompts, hits and cries come first.")]
    [Range(0f, 1f)] public float duelMusicVolume = 0.15f;
    [Range(0f, 1f)] public float effectsVolume = 1f;
    [Tooltip("Music volume during the clash, so the hits and war cries are heard.")]
    [Range(0f, 1f)] public float clashMusicDuck = 0.7f;
    [Tooltip("Music volume while the microphone records a war cry, so the cry is not the music played back.")]
    [Range(0f, 1f)] public float micMusicDuck = 0.1f;
    [Tooltip("Seconds to fade from the menu music to the duel music and back.")]
    public float crossfade = 1f;

    private static GameAudio instance;
    private readonly Dictionary<string, List<AudioClip>> clips = new Dictionary<string, List<AudioClip>>();
    private AudioSource effects;
    // Long sounds (K.O., victory) that must be cut short or faded instead of overlapping
    private AudioSource stinger;
    private Coroutine stingerFade;
    // Two music sources so one fades out while the other fades in
    private readonly AudioSource[] music = new AudioSource[2];
    private int currentMusic;
    private CardWeaponManager manager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance == null) new GameObject("Game Audio").AddComponent<GameAudio>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        foreach (var clip in Resources.LoadAll<AudioClip>("Audio"))
        {
            // "hit_2" -> "hit"
            string key = Regex.Replace(clip.name.ToLowerInvariant(), @"_\d+$", "");
            if (!clips.TryGetValue(key, out var list)) clips[key] = list = new List<AudioClip>();
            list.Add(clip);
        }
        effects = NewSource(false);
        stinger = NewSource(false);
        for (int i = 0; i < music.Length; i++) music[i] = NewSource(true);
    }

    private AudioSource NewSource(bool loop)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        return source;
    }

    // Switched in the main menu and remembered between sessions
    public static bool MusicEnabled
    {
        get => PlayerPrefs.GetInt("audio.music", 1) == 1;
        set { PlayerPrefs.SetInt("audio.music", value ? 1 : 0); PlayerPrefs.Save(); }
    }
    public static bool EffectsEnabled
    {
        get => PlayerPrefs.GetInt("audio.effects", 1) == 1;
        set { PlayerPrefs.SetInt("audio.effects", value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static bool Has(string name) => instance != null && instance.clips.ContainsKey(name);

    // One of the variants of `name`, picked at random
    public static void Play(string name, float volume = 1f)
    {
        if (instance == null || !EffectsEnabled || !instance.clips.TryGetValue(name, out var list) || list.Count == 0) return;
        instance.effects.PlayOneShot(list[Random.Range(0, list.Count)], volume * instance.effectsVolume);
    }

    // `name`, or `fallback` when that sound is not in the folder
    public static void Play(string name, string fallback, float volume = 1f) => Play(Has(name) ? name : fallback, volume);

    // A long sound on its own channel, replacing the one playing there; fades out after `seconds` (0: plays to the end)
    public static void PlayLong(string name, float seconds = 0f)
    {
        if (instance == null || !EffectsEnabled || !instance.clips.TryGetValue(name, out var list) || list.Count == 0) return;
        var source = instance.stinger;
        if (instance.stingerFade != null) instance.StopCoroutine(instance.stingerFade);
        instance.stingerFade = null;
        source.Stop();
        source.clip = list[Random.Range(0, list.Count)];
        source.volume = instance.effectsVolume;
        source.Play();
        if (seconds > 0f) instance.stingerFade = instance.StartCoroutine(instance.FadeLong(seconds, 0.5f));
    }

    // Fades out the long sound now
    public static void StopLong(float fade = 0.2f)
    {
        if (instance == null || !instance.stinger.isPlaying) return;
        if (instance.stingerFade != null) instance.StopCoroutine(instance.stingerFade);
        instance.stingerFade = instance.StartCoroutine(instance.FadeLong(0f, fade));
    }

    private System.Collections.IEnumerator FadeLong(float delay, float fade)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        float start = stinger.volume;
        for (float t = 0; t < fade; t += Time.unscaledDeltaTime)
        {
            stinger.volume = Mathf.Lerp(start, 0f, t / fade);
            yield return null;
        }
        stinger.Stop();
        stingerFade = null;
    }

    void Update()
    {
        if (manager == null) manager = FindAnyObjectByType<CardWeaponManager>();
        bool inMenu = ArenaMenu.IsOpen || manager == null || manager.SetupInProgress;
        string wanted = inMenu || !Has("music_battle") ? "music_menu" : "music_battle";
        AudioClip track = clips.TryGetValue(wanted, out var list) && list.Count > 0 ? list[0] : null;

        if (music[currentMusic].clip != track)
        {
            currentMusic = 1 - currentMusic;
            music[currentMusic].clip = track;
            music[currentMusic].volume = 0f;
            if (track != null) music[currentMusic].Play();
        }

        var duel = manager != null ? manager.Duel : null;
        bool clash = duel != null && duel.Match != null && duel.Match.Phase == DuelPhase.Clash;
        bool listening = duel != null && duel.Listening;
        float duck = listening ? micMusicDuck : clash ? clashMusicDuck : 1f;
        float target = MusicEnabled ? (inMenu ? musicVolume : duelMusicVolume) * duck : 0f;
        // Duck fast for the microphone: the cry starts right after the countdown
        float speed = listening ? 4f : 1f;
        float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, crossfade);
        for (int i = 0; i < music.Length; i++)
        {
            float goal = i == currentMusic ? target : 0f;
            music[i].volume = Mathf.MoveTowards(music[i].volume, goal, step * speed * Mathf.Max(musicVolume, 0.01f));
            if (i != currentMusic && music[i].isPlaying && music[i].volume <= 0f) music[i].Stop();
        }
    }
}
