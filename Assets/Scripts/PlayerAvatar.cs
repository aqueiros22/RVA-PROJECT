using UnityEngine;

// Added to a spawned GanzSe character: shows only the chosen armor parts and a name label facing the camera
public class PlayerAvatar : MonoBehaviour
{
    public int playerIndex;
    public bool showNameLabel = true;
    // Label height in the character's local units (the model is about 2 units tall)
    public float labelHeight = 2.3f;
    public Color labelColor = Color.yellow;
    public RuntimeAnimatorController animatorController;
    // Playback speed of the attack animations (heavy weapons swing slower)
    public float attackSpeed = 1f;
    public event System.Action<AttackDirection> attacked;
    public AttackDirection? LastAttack { get; private set; }

    public bool TryAttack(AttackDirection direction)
    {
        if (!HasWeapon || !isActiveAndEnabled) return false;
        LastAttack = direction;
        var animator = GetComponent<Animator>();
        if (HasParameter(animator, "AttackSpeed", AnimatorControllerParameterType.Float))
            animator.SetFloat("AttackSpeed", attackSpeed);
        PlayTrigger("Attack" + direction);
        attacked?.Invoke(direction);
        return true;
    }

    // Successful defense: vertical swings are blocked, side swings are ducked under
    public void PlayDefense(AttackDirection attack)
    {
        bool vertical = attack == AttackDirection.Up || attack == AttackDirection.Down;
        PlayTrigger(vertical ? "Block" : "Crouch");
    }

    public void PlayDeath()
    {
        PlayTrigger("Die");
    }

    // Fires a trigger, clearing pending ones so actions don't queue up.
    // Missing animations/parameters are allowed during prototyping.
    private void PlayTrigger(string trigger)
    {
        var animator = GetComponent<Animator>();
        if (!HasParameter(animator, trigger, AnimatorControllerParameterType.Trigger))
            return;
        foreach (var parameter in animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Trigger)
                animator.ResetTrigger(parameter.nameHash);
        animator.SetTrigger(trigger);
    }

    private static bool HasParameter(Animator animator, string name, AnimatorControllerParameterType type)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return false;
        foreach (var parameter in animator.parameters)
            if (parameter.type == type && parameter.name == name)
                return true;
        return false;
    }

    // Character height in its own local units, used to size the weapon in the hand
    private const float CharacterHeight = 2f;

    private Transform armorRoot;
    private Transform handBone;
    private GameObject equippedWeapon;
    private Transform faceRoot;
    private TextMesh label;
    private Camera mainCamera;
    private DuelController duel;
    private Transform healthFill;
    private SpriteRenderer healthFillRenderer;
    private static Sprite healthSprite;

    void Start()
    {
        duel = FindAnyObjectByType<DuelController>();
        armorRoot = transform.Find("ARMOR PARTS");
        faceRoot = transform.Find("FACE DETAILS PARTS");
        if (showNameLabel) CreateLabel();

        var animator = GetComponent<Animator>();
        if (animator != null && animatorController != null)
        {
            animator.runtimeAnimatorController = animatorController;
            animator.applyRootMotion = false;
        }

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

    public bool HasWeapon => equippedWeapon != null;

    // Puts a weapon in the right hand, replacing the current one
    public void Equip(GameObject weaponPrefab, float weaponBaseHeight, float heightFraction, Vector3 gripOffset, Quaternion gripRotation)
    {
        Unequip();
        if (handBone == null)
        {
            handBone = FindDeep(transform, "hand_r");
        }
        Transform parent = handBone != null ? handBone : transform;

        equippedWeapon = Instantiate(weaponPrefab, parent);
        equippedWeapon.name = weaponPrefab.name + " (Equipped)";
        // No spinning or floating while in the hand
        foreach (var spin in equippedWeapon.GetComponentsInChildren<RotatingWeapon>(true))
        {
            Destroy(spin);
        }

        Transform t = equippedWeapon.transform;
        t.localPosition = gripOffset;
        t.localRotation = gripRotation;

        // Size relative to the character, whatever the scale of the bones
        float parentToRoot = parent.lossyScale.y / Mathf.Max(0.0001f, transform.lossyScale.y);
        float localHeight = CharacterHeight * heightFraction / Mathf.Max(0.0001f, weaponBaseHeight);
        t.localScale = Vector3.one * (localHeight / Mathf.Max(0.0001f, parentToRoot));
        equippedWeapon.SetActive(true);
    }

    public void Unequip()
    {
        if (equippedWeapon != null)
        {
            Destroy(equippedWeapon);
            equippedWeapon = null;
        }
    }

    public void ResetCombat()
    {
        LastAttack = null;
        attackSpeed = 1;
        var animator = GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.Rebind();
            animator.Update(0);
        }
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
            {
                return child;
            }
            Transform found = FindDeep(child, name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
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
        if (duel != null)
        {
            if (healthSprite == null)
                healthSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
            var background = new GameObject("Health Bar");
            background.transform.SetParent(label.transform, false);
            background.transform.localPosition = new Vector3(0, -0.1f, 0);
            background.transform.localScale = new Vector3(0.9f, 0.055f, 1);
            var backgroundRenderer = background.AddComponent<SpriteRenderer>();
            backgroundRenderer.sprite = healthSprite;
            backgroundRenderer.color = new Color(0.3f, 0.08f, 0.08f);
            var fill = new GameObject("Health Fill");
            fill.transform.SetParent(background.transform, false);
            healthFill = fill.transform;
            healthFillRenderer = fill.AddComponent<SpriteRenderer>();
            healthFillRenderer.sprite = healthSprite;
            healthFillRenderer.sortingOrder = 1;
        }
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

        if (duel != null && duel.Match != null && PlayerProfiles.Instance != null)
        {
            int hp = duel.Match.Health(playerIndex);
            label.text = PlayerProfiles.Instance.Get(playerIndex).name + $"\n{hp} / 100 HP";
            label.color = hp <= 25 ? Color.red : labelColor;
            if (healthFill != null)
            {
                float fraction = hp / 100f;
                healthFill.localScale = new Vector3(fraction, 1, 1);
                healthFill.localPosition = new Vector3((fraction - 1) * 0.5f, 0, -0.01f);
                healthFillRenderer.color = hp <= 25 ? Color.red : Color.green;
            }
        }

        // Billboard: always face the camera
        Transform labelTransform = label.transform;
        labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - mainCamera.transform.position, mainCamera.transform.up);
    }
}
