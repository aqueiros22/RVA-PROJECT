using System;
using UnityEngine;

// Holds each player's name and armor choice, shared by the setup UI and the spawned characters
public class PlayerProfiles : MonoBehaviour
{
    // Same order as the children of "ARMOR PARTS" in the GanzSe character
    public static readonly string[] ArmorCategories = { "HEADS", "CHESTS", "ARMS", "BELTS", "LEGS", "FEET" };

    [Serializable]
    public class Profile
    {
        public string name;
        public int[] armor = new int[ArmorCategories.Length];
        public bool showHelmet = true;
    }

    public static PlayerProfiles Instance { get; private set; }

    public Profile[] players =
    {
        new Profile { name = "Jogador 1", armor = new[] { 0, 0, 0, 0, 0, 0 } },
        new Profile { name = "Jogador 2", armor = new[] { 1, 1, 1, 1, 1, 1 } }
    };

    public event Action<int> changed;

    void Awake()
    {
        Instance = this;
        Load();
    }

    public Profile Get(int playerIndex)
    {
        return players[Mathf.Clamp(playerIndex, 0, players.Length - 1)];
    }

    public void NotifyChanged(int playerIndex)
    {
        Save(playerIndex);
        changed?.Invoke(playerIndex);
    }

    // Remember choices between sessions
    void Save(int i)
    {
        var p = players[i];
        PlayerPrefs.SetString($"player{i}.name", p.name);
        PlayerPrefs.SetString($"player{i}.armor", string.Join(",", p.armor));
        PlayerPrefs.SetInt($"player{i}.helmet", p.showHelmet ? 1 : 0);
        PlayerPrefs.Save();
    }

    void Load()
    {
        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            p.name = PlayerPrefs.GetString($"player{i}.name", p.name);
            p.showHelmet = PlayerPrefs.GetInt($"player{i}.helmet", p.showHelmet ? 1 : 0) == 1;
            string armor = PlayerPrefs.GetString($"player{i}.armor", "");
            if (armor.Length == 0)
            {
                continue;
            }
            string[] parts = armor.Split(',');
            for (int c = 0; c < p.armor.Length && c < parts.Length; c++)
            {
                int.TryParse(parts[c], out p.armor[c]);
            }
        }
    }
}
