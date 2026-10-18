using UnityEngine;

// Added to a spawned GanzSe character: shows only the chosen armor parts and a name label facing the camera
public class PlayerAvatar : MonoBehaviour
{
    public int playerIndex;
    // Label height in the character's local units (the model is about 2 units tall)
    public float labelHeight = 2.3f;
    public Color labelColor = Color.yellow;

    private Transform armorRoot;
    private Transform faceRoot;
    private TextMesh label;
    private Camera mainCamera;

    void Start()
    {
        armorRoot = transform.Find("ARMOR PARTS");
        faceRoot = transform.Find("FACE DETAILS PARTS");
        CreateLabel();

        if (PlayerProfiles.Instance != null)
        {
            PlayerProfiles.Instance.changed += OnProfileChanged;
        }
        Apply();
    }

    void OnDestroy()
    {
        if (PlayerProfiles.Instance != null)
        {
            PlayerProfiles.Instance.changed -= OnProfileChanged;
        }
    }

    private void OnProfileChanged(int index)
    {
        if (index == playerIndex)
        {
            Apply();
        }
    }

    public void Apply()
    {
        if (PlayerProfiles.Instance == null)
        {
            return;
        }
        var profile = PlayerProfiles.Instance.Get(playerIndex);

        if (armorRoot != null)
        {
            for (int c = 0; c < PlayerProfiles.ArmorCategories.Length; c++)
            {
                Transform category = armorRoot.Find(PlayerProfiles.ArmorCategories[c]);
                if (category != null)
                {
                    ShowOnly(category, profile.armor[c]);
                }
            }

            Transform heads = armorRoot.Find("HEADS");
            if (heads != null)
            {
                heads.gameObject.SetActive(profile.showHelmet);
            }
        }

        // Without a helmet, show one of each face detail instead of all of them stacked
        if (faceRoot != null)
        {
            faceRoot.gameObject.SetActive(!profile.showHelmet);
            foreach (Transform category in faceRoot)
            {
                ShowOnly(category, playerIndex);
            }
        }

        if (label != null)
        {
            label.text = profile.name;
        }
    }

    private static void ShowOnly(Transform category, int index)
    {
        if (category.childCount == 0)
        {
            return;
        }
        int selected = ((index % category.childCount) + category.childCount) % category.childCount;
        for (int i = 0; i < category.childCount; i++)
        {
            category.GetChild(i).gameObject.SetActive(i == selected);
        }
    }

    private void CreateLabel()
    {
        var labelObject = new GameObject("Name Label");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, labelHeight, 0f);

        label = labelObject.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.font = font;
        label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        label.fontSize = 64;
        label.characterSize = 0.04f;
        label.fontStyle = FontStyle.Bold;
        label.anchor = TextAnchor.LowerCenter;
        label.alignment = TextAlignment.Center;
        label.color = labelColor;
    }

    void LateUpdate()
    {
        if (label == null)
        {
            return;
        }
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }
        }

        // Billboard: always face the camera
        Transform labelTransform = label.transform;
        labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - mainCamera.transform.position, mainCamera.transform.up);
    }
}
