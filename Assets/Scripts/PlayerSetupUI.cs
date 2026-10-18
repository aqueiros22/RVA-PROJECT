using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Builds, at runtime, a panel where each player picks a name and armor parts
[RequireComponent(typeof(PlayerProfiles))]
public class PlayerSetupUI : MonoBehaviour
{
    // Used to know how many options each armor category has
    public GameObject playerPrefab;
    public bool openOnStart = true;

    private static readonly string[] CategoryLabels = { "Capacete", "Peito", "Braços", "Cinto", "Pernas", "Pés" };

    private PlayerProfiles profiles;
    private Font font;
    private GameObject panel;
    private int currentPlayer;
    private int[] optionCounts;

    private InputField nameField;
    private Text[] optionTexts;
    private Text helmetText;
    private Image[] tabImages;

    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Color ButtonColor = new Color(0.22f, 0.22f, 0.22f, 1f);
    private static readonly Color SelectedColor = new Color(0.85f, 0.65f, 0.1f, 1f);

    void Start()
    {
        profiles = GetComponent<PlayerProfiles>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (playerPrefab == null)
        {
            var manager = FindFirstObjectByType<CardWeaponManager>();
            if (manager != null)
            {
                playerPrefab = manager.playerPrefab;
            }
        }
        optionCounts = CountOptions();

        EnsureEventSystem();
        BuildUI();
        panel.SetActive(openOnStart);
        SelectPlayer(0);
    }

    private int[] CountOptions()
    {
        var counts = new int[PlayerProfiles.ArmorCategories.Length];
        Transform armorRoot = playerPrefab != null ? playerPrefab.transform.Find("ARMOR PARTS") : null;
        for (int c = 0; c < counts.Length; c++)
        {
            Transform category = armorRoot != null ? armorRoot.Find(PlayerProfiles.ArmorCategories[c]) : null;
            counts[c] = category != null ? Mathf.Max(1, category.childCount) : 1;
        }
        return counts;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }
        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(eventSystem);
    }

    // ---------- UI construction ----------

    private void BuildUI()
    {
        var canvasObject = new GameObject("Player Setup Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasObject.transform;

        // Button that opens/closes the panel
        var toggle = CreateButton(root, "Personalizar", () => panel.SetActive(!panel.activeSelf));
        SetRect(toggle.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), new Vector2(330, 110), new Vector2(1, 1));

        // Panel at the bottom of the screen
        panel = CreateImage(root, "Panel", PanelColor).gameObject;
        SetRect(panel.transform, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 1150), new Vector2(0.5f, 0));
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 30, 30);
        layout.spacing = 18;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        // Player tabs
        var tabs = CreateRow(panel.transform);
        tabImages = new Image[profiles.players.Length];
        for (int i = 0; i < profiles.players.Length; i++)
        {
            int index = i;
            var tab = CreateButton(tabs, $"Jogador {i + 1}", () => SelectPlayer(index));
            tabImages[i] = tab.GetComponent<Image>();
        }

        // Name
        var nameRow = CreateRow(panel.transform);
        CreateText(nameRow, "Nome", 44, TextAnchor.MiddleLeft, 220);
        nameField = CreateInputField(nameRow);
        nameField.onEndEdit.AddListener(OnNameChanged);

        // Armor categories
        optionTexts = new Text[CategoryLabels.Length];
        for (int c = 0; c < CategoryLabels.Length; c++)
        {
            int category = c;
            var row = CreateRow(panel.transform);
            CreateText(row, CategoryLabels[c], 44, TextAnchor.MiddleLeft, 220);
            CreateButton(row, "<", () => ChangeArmor(category, -1), 140);
            optionTexts[c] = CreateText(row, "", 44, TextAnchor.MiddleCenter);
            CreateButton(row, ">", () => ChangeArmor(category, 1), 140);
        }

        // Helmet toggle
        var helmetRow = CreateRow(panel.transform);
        var helmetButton = CreateButton(helmetRow, "", ToggleHelmet);
        helmetText = helmetButton.GetComponentInChildren<Text>();

        // Actions
        var actions = CreateRow(panel.transform);
        CreateButton(actions, "Aleatório", Randomize);
        var done = CreateButton(actions, "Pronto", () => panel.SetActive(false));
        done.GetComponent<Image>().color = SelectedColor;
    }

    private RectTransform CreateRow(Transform parent)
    {
        var row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 16;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        var element = row.AddComponent<LayoutElement>();
        element.preferredHeight = 110;
        return row.GetComponent<RectTransform>();
    }

    private Image CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private Text CreateText(Transform parent, string content, int size, TextAnchor anchor, float width = -1)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        if (width > 0)
        {
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleWidth = 0;
        }
        return text;
    }

    private Button CreateButton(Transform parent, string content, UnityEngine.Events.UnityAction onClick, float width = -1)
    {
        var image = CreateImage(parent, "Button", ButtonColor);
        var button = image.gameObject.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        var text = CreateText(image.transform, content, 44, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        if (width > 0)
        {
            var element = image.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleWidth = 0;
        }
        return button;
    }

    private InputField CreateInputField(Transform parent)
    {
        var image = CreateImage(parent, "Name Input", Color.white);
        var input = image.gameObject.AddComponent<InputField>();
        var text = CreateText(image.transform, "", 44, TextAnchor.MiddleLeft);
        text.color = Color.black;
        text.supportRichText = false;
        Stretch(text.rectTransform, 20);
        input.textComponent = text;
        input.characterLimit = 16;
        return input;
    }

    private static void Stretch(RectTransform rect, float padding = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, 0);
        rect.offsetMax = new Vector2(-padding, 0);
    }

    private static void SetRect(Transform t, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2 pivot)
    {
        var rect = (RectTransform)t;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // ---------- Behaviour ----------

    private void SelectPlayer(int index)
    {
        currentPlayer = index;
        for (int i = 0; i < tabImages.Length; i++)
        {
            tabImages[i].color = i == index ? SelectedColor : ButtonColor;
        }
        Refresh();
    }

    private void Refresh()
    {
        var profile = profiles.Get(currentPlayer);
        nameField.SetTextWithoutNotify(profile.name);
        for (int c = 0; c < optionTexts.Length; c++)
        {
            optionTexts[c].text = $"{Wrap(profile.armor[c], optionCounts[c]) + 1} / {optionCounts[c]}";
        }
        helmetText.text = profile.showHelmet ? "Capacete: Sim" : "Capacete: Não";
    }

    private static int Wrap(int value, int count)
    {
        return ((value % count) + count) % count;
    }

    private void OnNameChanged(string value)
    {
        var profile = profiles.Get(currentPlayer);
        profile.name = string.IsNullOrWhiteSpace(value) ? $"Jogador {currentPlayer + 1}" : value.Trim();
        profiles.NotifyChanged(currentPlayer);
        Refresh();
    }

    private void ChangeArmor(int category, int delta)
    {
        var profile = profiles.Get(currentPlayer);
        profile.armor[category] = Wrap(profile.armor[category] + delta, optionCounts[category]);
        profiles.NotifyChanged(currentPlayer);
        Refresh();
    }

    private void ToggleHelmet()
    {
        var profile = profiles.Get(currentPlayer);
        profile.showHelmet = !profile.showHelmet;
        profiles.NotifyChanged(currentPlayer);
        Refresh();
    }

    private void Randomize()
    {
        var profile = profiles.Get(currentPlayer);
        for (int c = 0; c < profile.armor.Length; c++)
        {
            profile.armor[c] = Random.Range(0, optionCounts[c]);
        }
        profiles.NotifyChanged(currentPlayer);
        Refresh();
    }
}
