using System;
using UnityEngine;

public enum GameLanguage
{
    English,
    Portuguese
}

public static class Localization
{
    private const string PrefKey = "ArenaGameLanguage";

    public static GameLanguage CurrentLanguage { get; private set; } = GameLanguage.English;

    public static event Action OnLanguageChanged;

    static Localization()
    {
        int saved = PlayerPrefs.GetInt(PrefKey, (int)GameLanguage.English);
        CurrentLanguage = (GameLanguage)saved;
    }

    public static void SetLanguage(GameLanguage language)
    {
        CurrentLanguage = language;
        PlayerPrefs.SetInt(PrefKey, (int)language);
        PlayerPrefs.Save();
        OnLanguageChanged?.Invoke();
    }

    public static string Get(string english, string portuguese)
    {
        return CurrentLanguage == GameLanguage.Portuguese ? portuguese : english;
    }
}
