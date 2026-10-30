using UnityEngine;
using UnityEngine.UI;

// Damage number that pops out of where the blade connected and floats up, following the AR scene:
// a big "-45" and, under it, CRITICAL! / Not very effective... / BLOCKED / K.O.!
public class FloatingCombatText : MonoBehaviour
{
    public float duration = 1.4f;
    public float rise = 110f;

    private Vector3 worldPoint;
    private RectTransform rect;
    private CanvasGroup group;
    private Vector2 drift;
    private float start;

    public static FloatingCombatText Spawn(Transform canvas, Vector3 worldPoint, string value, Color valueColor, string tag, Color tagColor)
    {
        var obj = new GameObject("Floating Combat Text", typeof(RectTransform), typeof(CanvasGroup));
        obj.transform.SetParent(canvas, false);
        var rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.sizeDelta = new Vector2(420, 150);
        obj.GetComponent<CanvasGroup>().blocksRaycasts = false;

        bool hasTag = !string.IsNullOrEmpty(tag);
        var number = Label(rect, value, 72, valueColor);
        Place(number.rectTransform, hasTag ? 0.38f : 0f, 1f);
        if (hasTag) Place(Label(rect, tag, 30, tagColor).rectTransform, 0f, 0.4f);

        var text = obj.AddComponent<FloatingCombatText>();
        text.worldPoint = worldPoint;
        text.rect = rect;
        text.group = obj.GetComponent<CanvasGroup>();
        // A little sideways drift so two hits in a row do not overlap
        text.drift = new Vector2(Random.Range(-35f, 35f), 0f);
        text.start = Time.unscaledTime;
        text.Update();
        return text;
    }

    void Update()
    {
        float t = (Time.unscaledTime - start) / duration;
        if (t >= 1f) { Destroy(gameObject); return; }

        Camera camera = Camera.main;
        Vector3 screen = camera != null ? camera.WorldToScreenPoint(worldPoint) : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 1f);
        // Behind the camera: nothing to show
        group.alpha = screen.z <= 0f ? 0f : 1f - Mathf.Clamp01((t - 0.65f) / 0.35f);

        // Screen pixels to canvas units (the HUD canvas scales with the screen)
        float scale = rect.parent is RectTransform parent && parent.rect.width > 0 ? parent.rect.width / Screen.width : 1f;
        float eased = 1f - (1f - t) * (1f - t);
        rect.anchoredPosition = new Vector2(screen.x, screen.y) * scale + (Vector2.up * rise + drift) * eased;
        // Pop: starts big and settles
        float pop = t < 0.15f ? Mathf.Lerp(1.6f, 1f, t / 0.15f) : 1f;
        rect.localScale = Vector3.one * pop;
    }

    private static Text Label(Transform parent, string content, int size, Color color)
    {
        var obj = new GameObject("Text", typeof(RectTransform), typeof(Text));
        obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.color = color;
        text.text = content;
        text.raycastTarget = false;
        var outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0.9f);
        outline.effectDistance = new Vector2(3, -3);
        obj.AddComponent<Shadow>().effectDistance = new Vector2(0, -5);
        return text;
    }

    // Vertical band of the text box, from bottom to top (0-1)
    private static void Place(RectTransform rect, float bottom, float top)
    {
        rect.anchorMin = new Vector2(0, bottom);
        rect.anchorMax = new Vector2(1, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
