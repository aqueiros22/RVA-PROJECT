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
        Label(root.transform, "ARena <color=#FFCB71>Clash</color>", 78, 0.72f, 0.89f);
        Label(root.transform, "YOUR TABLE. YOUR ARENA.", 20, 0.65f, 0.71f);
        home = Page("Main menu");
        modes = Page("Match modes");
        Button(home.transform, "PLAY", 0.47f, () => { home.SetActive(false); modes.SetActive(true); });
        Button(home.transform, "EXIT", 0.32f, Exit);
        Label(modes.transform, "CHOOSE YOUR MATCH", 25, 0.56f, 0.63f);
        Button(modes.transform, "P v P    ·    Player vs Player", 0.46f, () => Play(ArenaMode.PlayerVsPlayer));
        Button(modes.transform, "P v C    ·    Player vs Computer", 0.32f, () => Play(ArenaMode.PlayerVsComputer));
        Button(modes.transform, "C v C    ·    Computer vs Computer", 0.18f, () => Play(ArenaMode.ComputerVsComputer));
        Button(modes.transform, "BACK", 0.04f, () => { modes.SetActive(false); home.SetActive(true); });
        modes.SetActive(false);
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

    private void Button(Transform parent, string text, float y, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.28f, y);
        rect.anchorMax = new Vector2(0.72f, y + 0.105f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = new Color(0.12f, 0.19f, 0.26f, 0.96f);
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.95f, 0.68f, 0.3f, 0.85f);
        outline.effectDistance = new Vector2(2, -2);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        var colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.8f, 0.45f);
        colors.pressedColor = new Color(0.7f, 0.5f, 0.25f);
        button.colors = colors;
        Label(go.transform, text, 30, 0, 1);
    }

    private void Label(Transform parent, string content, int size, float bottom, float top)
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
    }

    private static void Fill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
