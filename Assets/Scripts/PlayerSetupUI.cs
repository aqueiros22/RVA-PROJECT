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
    public bool IsComplete { get; private set; }
    public bool IsCustomizing => panel != null && panel.activeSelf;
    private CardWeaponManager manager;
    private Text scanPrompt;
    private Text title;
    private GameObject previewRoot;
    private PlayerAvatar previewAvatar;
    private Camera previewCamera;
    private RenderTexture previewTexture;
    private Camera worldCamera;
    private int worldCameraMask;
    private const int PreviewLayer = 31;

    private static readonly string[] CategoryLabels = { "Helmet", "Chest", "Arms", "Belt", "Legs", "Feet" };

    private PlayerProfiles profiles;
    private Font font;
    private GameObject panel;
    private int currentPlayer;
    private int[] optionCounts;

    private InputField nameField;
    private Text nameLabelText;
    private Text[] categoryLabelTexts;
    private Text randomizeButtonText;
    private Text doneButtonText;
    private Text[] optionTexts;
    private Text helmetText;

    private static readonly Color ButtonColor = new Color(0.22f, 0.22f, 0.22f, 1f);
    private static readonly Color SelectedColor = new Color(0.85f, 0.65f, 0.1f, 1f);

    private static string GetCategoryLabel(int index)
    {
        switch (index)
        {
            case 0: return Localization.Get("Helmet", "Capacete");
            case 1: return Localization.Get("Chest", "Peitoral");
            case 2: return Localization.Get("Arms", "Braços");
            case 3: return Localization.Get("Belt", "Cinto");
            case 4: return Localization.Get("Legs", "Pernas");
            case 5: return Localization.Get("Feet", "Pés");
            default: return "";
        }
    }

    void Start()
    {
        Localization.OnLanguageChanged += UpdateLocalizedTexts;
        profiles = GetComponent<PlayerProfiles>();
        manager = FindFirstObjectByType<CardWeaponManager>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (playerPrefab == null)
        {
            if (manager != null)
            {
                playerPrefab = manager.playerPrefab;
            }
        }
        optionCounts = CountOptions();

        EnsureEventSystem();
        BuildUI();
        WaitForPlayer(0);
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

        scanPrompt = CreateText(root, "", 52, TextAnchor.MiddleCenter);
        SetRect(scanPrompt.transform, new Vector2(0, 0.4f), new Vector2(1, 0.6f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        scanPrompt.raycastTarget = false;

        panel = CreateImage(root, "Customization", new Color(0.045f, 0.055f, 0.08f, 1f)).gameObject;
        Stretch((RectTransform)panel.transform);

        var viewport = new GameObject("Preview Area", typeof(RectTransform));
        viewport.transform.SetParent(panel.transform, false);
        SetRect(viewport.transform, new Vector2(0.5f, 0.08f), new Vector2(1, 0.92f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        var preview = new GameObject("Character Preview", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        preview.transform.SetParent(viewport.transform, false);
        var aspect = preview.GetComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = 768f / 1280f;
        preview.GetComponent<RawImage>().raycastTarget = false;
        BuildPreview(preview.GetComponent<RawImage>());

        var controls = new GameObject("Controls", typeof(RectTransform));
        controls.transform.SetParent(panel.transform, false);
        SetRect(controls.transform, new Vector2(0, 0), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        var layout = controls.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 30, 30);
        layout.spacing = 14;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = true;
        Transform content = controls.transform;
        title = CreateText(CreateRow(content), "", 40, TextAnchor.MiddleCenter);

        // Name
        var nameRow = CreateRow(content);
        nameLabelText = CreateText(nameRow, Localization.Get("Name", "Nome"), 32, TextAnchor.MiddleLeft, 115);
        nameField = CreateInputField(nameRow);
        nameField.onEndEdit.AddListener(OnNameChanged);

        // Armor categories
        optionTexts = new Text[CategoryLabels.Length];
        categoryLabelTexts = new Text[CategoryLabels.Length];
        for (int c = 0; c < CategoryLabels.Length; c++)
        {
            int category = c;
            var row = CreateRow(content);
            categoryLabelTexts[c] = CreateText(row, GetCategoryLabel(c), 30, TextAnchor.MiddleLeft, 115);
            CreateButton(row, "<", () => ChangeArmor(category, -1), 60);
            optionTexts[c] = CreateText(row, "", 30, TextAnchor.MiddleCenter);
            CreateButton(row, ">", () => ChangeArmor(category, 1), 60);
        }

        // Helmet toggle
        var helmetRow = CreateRow(content);
        var helmetButton = CreateButton(helmetRow, "", ToggleHelmet);
        helmetText = helmetButton.GetComponentInChildren<Text>();

        // Actions
        var actions = CreateRow(content);
        var randBtn = CreateButton(actions, Localization.Get("Randomize", "Aleatório"), Randomize);
        randomizeButtonText = randBtn.GetComponentInChildren<Text>();
        var done = CreateButton(actions, Localization.Get("OK", "Pronto"), ConfirmPlayer);
        doneButtonText = done.GetComponentInChildren<Text>();
        done.GetComponent<Image>().color = SelectedColor;
    }

    private RectTransform CreateRow(Transform parent)
    {
        var row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        var element = row.AddComponent<LayoutElement>();
        element.preferredHeight = 90;
        element.flexibleHeight = 1;
        element.minHeight = 32;
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
        var text = CreateText(image.transform, content, 30, TextAnchor.MiddleCenter);
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
        var text = CreateText(image.transform, "", 30, TextAnchor.MiddleLeft);
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

    void Update()
    {
        if (ArenaMenu.IsOpen)
        {
            scanPrompt.gameObject.SetActive(false);
            return;
        }
        if (!IsComplete && ArenaMenu.IsComputer(currentPlayer) && manager != null && manager.IsPlayerTracked(currentPlayer))
        {
            Randomize();
            ConfirmPlayer();
            return;
        }
        if (Time.timeScale == 0f)
        {
            if (scanPrompt != null && scanPrompt.gameObject.activeSelf)
                scanPrompt.gameObject.SetActive(false);
            return;
        }
        else if (!IsComplete && !panel.activeSelf && scanPrompt != null && !scanPrompt.gameObject.activeSelf)
        {
            scanPrompt.gameObject.SetActive(true);
        }

        if (!IsComplete && !ArenaMenu.IsComputer(currentPlayer) && !panel.activeSelf && previewAvatar != null && manager != null && manager.IsPlayerTracked(currentPlayer))
        {
            scanPrompt.gameObject.SetActive(false);
            panel.SetActive(true);
            previewAvatar.playerIndex = currentPlayer;
            previewRoot.SetActive(true);
            previewAvatar.Apply();
            previewCamera.gameObject.SetActive(true);
            title.text = Localization.Get(
                $"Customize Player {currentPlayer + 1}",
                $"Personalizar Jogador {currentPlayer + 1}");
            Refresh();
        }
    }

    private void WaitForPlayer(int index)
    {
        currentPlayer = index;
        panel.SetActive(false);
        if (previewRoot != null) previewRoot.SetActive(false);
        if (previewCamera != null) previewCamera.gameObject.SetActive(false);
        scanPrompt.text = Localization.Get(
            $"Please scan the PLAYER {index + 1} card.",
            $"Por favor, aponte para a carta JOGADOR {index + 1}.");
        scanPrompt.gameObject.SetActive(true);
    }

    public void ResetSetup()
    {
        IsComplete = false;
        WaitForPlayer(0);
    }

    private void ConfirmPlayer()
    {
        OnNameChanged(nameField.text);
        if (currentPlayer == 0)
        {
            WaitForPlayer(1);
            return;
        }
        panel.SetActive(false);
        previewRoot.SetActive(false);
        previewCamera.gameObject.SetActive(false);
        scanPrompt.gameObject.SetActive(false);
        IsComplete = true;
    }

    private void BuildPreview(RawImage image)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("Player setup requires a character prefab.", this);
            return;
        }
        previewRoot = Instantiate(playerPrefab, new Vector3(10000, 10000, 10000), Quaternion.Euler(0, 180, 0));
        previewRoot.name = "Setup Character Preview";
        previewRoot.SetActive(true);
        foreach (Transform child in previewRoot.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = PreviewLayer;
        previewAvatar = previewRoot.GetComponent<PlayerAvatar>();
        if (previewAvatar == null) previewAvatar = previewRoot.AddComponent<PlayerAvatar>();
        previewAvatar.showNameLabel = false;
        previewAvatar.animatorController = manager != null ? manager.playerAnimator : null;

        var renderers = previewRoot.GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds(previewRoot.transform.position + Vector3.up, new Vector3(1, 2, 1));
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        }
        previewTexture = new RenderTexture(768, 1280, 24);
        previewTexture.Create();
        image.texture = previewTexture;
        var cameraObject = new GameObject("Setup Preview Camera", typeof(Camera));
        cameraObject.transform.SetParent(transform, false);
        previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.045f, 0.055f, 0.08f, 1f);
        previewCamera.targetTexture = previewTexture;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / previewCamera.aspect) * 1.2f;
        previewCamera.nearClipPlane = 0.01f;
        previewCamera.farClipPlane = Mathf.Max(20, bounds.size.z * 4);
        cameraObject.transform.position = bounds.center - Vector3.forward * (bounds.extents.z + 5);
        cameraObject.transform.rotation = Quaternion.identity;
        worldCamera = Camera.main;
        if (worldCamera != null)
        {
            worldCameraMask = worldCamera.cullingMask;
            worldCamera.cullingMask &= ~(1 << PreviewLayer);
        }
    }

    void OnDestroy()
    {
        Localization.OnLanguageChanged -= UpdateLocalizedTexts;
        if (worldCamera != null) worldCamera.cullingMask = worldCameraMask;
        if (previewCamera != null) Destroy(previewCamera.gameObject);
        if (previewRoot != null) Destroy(previewRoot);
        if (previewTexture != null)
        {
            previewTexture.Release();
            Destroy(previewTexture);
        }
    }

    private void UpdateLocalizedTexts()
    {
        if (title != null)
        {
            title.text = Localization.Get(
                $"Customize Player {currentPlayer + 1}",
                $"Personalizar Jogador {currentPlayer + 1}");
        }
        if (scanPrompt != null)
        {
            scanPrompt.text = Localization.Get(
                $"Please scan the PLAYER {currentPlayer + 1} card.",
                $"Por favor, aponte para a carta JOGADOR {currentPlayer + 1}.");
        }
        if (nameLabelText != null)
        {
            nameLabelText.text = Localization.Get("Name", "Nome");
        }
        if (categoryLabelTexts != null)
        {
            for (int i = 0; i < categoryLabelTexts.Length; i++)
            {
                if (categoryLabelTexts[i] != null)
                    categoryLabelTexts[i].text = GetCategoryLabel(i);
            }
        }
        if (randomizeButtonText != null)
        {
            randomizeButtonText.text = Localization.Get("Randomize", "Aleatório");
        }
        if (doneButtonText != null)
        {
            doneButtonText.text = Localization.Get("OK", "Pronto");
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
        helmetText.text = profile.showHelmet
            ? Localization.Get("Helmet: On", "Capacete: Sim")
            : Localization.Get("Helmet: Off", "Capacete: Não");
    }

    private static int Wrap(int value, int count)
    {
        return ((value % count) + count) % count;
    }

    private void OnNameChanged(string value)
    {
        var profile = profiles.Get(currentPlayer);
        string defaultName = Localization.Get($"Player {currentPlayer + 1}", $"Jogador {currentPlayer + 1}");
        profile.name = string.IsNullOrWhiteSpace(value) ? defaultName : value.Trim();
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
