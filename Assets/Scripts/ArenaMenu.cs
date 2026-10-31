using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public enum ArenaMode { PlayerVsPlayer, PlayerVsComputer, ComputerVsComputer }

// Installed by the arena manager, so existing scenes need no manual wiring.
public class ArenaMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; } = true;
    public static ArenaMode Mode { get; private set; }
    public static bool IsComputer(int player) => Mode == ArenaMode.ComputerVsComputer ||
        (Mode == ArenaMode.PlayerVsComputer && player == 1);
    private GameObject root, home, modes;
    private Text musicLabel, effectsLabel;
    private Font font;

    void Awake() { IsOpen = true; Mode = ArenaMode.PlayerVsPlayer; }

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("Menu EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        root = new GameObject("ARena Clash Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        canvas.sortingOrder = 100;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 900);
        scaler.matchWidthOrHeight = 0.5f;
        var backdrop = new GameObject("Arena artwork", typeof(RectTransform), typeof(RawImage));
        backdrop.transform.SetParent(root.transform, false);
        Fill(backdrop.GetComponent<RectTransform>());
        // Extend past the screen edges to avoid camera seams from canvas scaling.
        var backgroundRect = backdrop.GetComponent<RectTransform>();
        backgroundRect.offsetMin = new Vector2(-4f, -4f);
        backgroundRect.offsetMax = new Vector2(4f, 4f);
        var image = backdrop.GetComponent<RawImage>();
        image.texture = Resources.Load<Texture2D>("Menu/arena-clash-background");
        image.color = image.texture != null ? Color.white : new Color(0.035f, 0.06f, 0.11f);
        var shade = new GameObject("Shade", typeof(RectTransform), typeof(Image));
        shade.transform.SetParent(root.transform, false);
        Fill(shade.GetComponent<RectTransform>());
        shade.GetComponent<Image>().color = new Color(0.015f, 0.025f, 0.05f, 0.58f);
        // Sized for a phone held sideways: big title, compact buttons that leave the background visible
        Label(root.transform, "ARena <color=#FFCB71>Clash</color>", 110, 0.75f, 0.95f);
        Label(root.transform, "YOUR TABLE. YOUR ARENA.", 30, 0.67f, 0.75f);
        home = Page("Main menu");
        modes = Page("Match modes");
        Button(home.transform, "PLAY", 0.44f, 0.08f, () => { home.SetActive(false); modes.SetActive(true); });
        Button(home.transform, "EXIT", 0.34f, 0.08f, Exit);
        // Sound switches, remembered between sessions
        musicLabel = Button(home.transform, "", 0.24f, 0.07f, ToggleMusic, 0.38f, 0.495f);
        effectsLabel = Button(home.transform, "", 0.24f, 0.07f, ToggleEffects, 0.505f, 0.62f);
        RefreshSoundLabels();
        Label(modes.transform, "CHOOSE YOUR MATCH", 28, 0.55f, 0.62f);
        Button(modes.transform, "P v P    ·    Player vs Player", 0.44f, 0.08f, () => Play(ArenaMode.PlayerVsPlayer), 0.33f, 0.67f);
        Button(modes.transform, "P v C    ·    Player vs Computer", 0.34f, 0.08f, () => Play(ArenaMode.PlayerVsComputer), 0.33f, 0.67f);
        Button(modes.transform, "C v C    ·    Computer vs Computer", 0.24f, 0.08f, () => Play(ArenaMode.ComputerVsComputer), 0.33f, 0.67f);
        Button(modes.transform, "BACK", 0.12f, 0.07f, () => { modes.SetActive(false); home.SetActive(true); }, 0.44f, 0.56f);
        modes.SetActive(false);
    }

    private void ToggleMusic()
    {
        GameAudio.MusicEnabled = !GameAudio.MusicEnabled;
        RefreshSoundLabels();
    }

    private void ToggleEffects()
    {
        GameAudio.EffectsEnabled = !GameAudio.EffectsEnabled;
        RefreshSoundLabels();
        // Let the player hear that sound is back
        GameAudio.Play("click");
    }

    private void RefreshSoundLabels()
    {
        musicLabel.text = GameAudio.MusicEnabled ? "MUSIC: ON" : "MUSIC: OFF";
        effectsLabel.text = GameAudio.EffectsEnabled ? "SOUND FX: ON" : "SOUND FX: OFF";
    }

    private GameObject Page(string name)
    {
        var page = new GameObject(name, typeof(RectTransform));
        page.transform.SetParent(root.transform, false);
        Fill(page.GetComponent<RectTransform>());
        return page;
    }

    private void Play(ArenaMode mode)
    {
        Mode = mode;
        IsOpen = false;
        root.SetActive(false);
    }

    public void Open()
    {
        IsOpen = true;
        root.SetActive(true);
        home.SetActive(true);
        modes.SetActive(false);
    }

    private static void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // y and height are fractions of the screen height; left/right of its width
    private Text Button(Transform parent, string text, float y, float height, UnityEngine.Events.UnityAction action,
        float left = 0.38f, float right = 0.62f)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(left, y);
        rect.anchorMax = new Vector2(right, y + height);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = new Color(0.12f, 0.19f, 0.26f, 0.96f);
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.95f, 0.68f, 0.3f, 0.85f);
        outline.effectDistance = new Vector2(2, -2);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(() => GameAudio.Play("click"));
        button.onClick.AddListener(action);
        var colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.8f, 0.45f);
        colors.pressedColor = new Color(0.7f, 0.5f, 0.25f);
        button.colors = colors;
        return Label(go.transform, text, 24, 0, 1);
    }

    private Text Label(Transform parent, string content, int size, float bottom, float top)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.03f, bottom);
        rect.anchorMax = new Vector2(0.97f, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var text = go.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 12;
        text.resizeTextMaxSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(1f, 0.94f, 0.82f);
        text.raycastTarget = false;
        return text;
    }

    private static void Fill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
