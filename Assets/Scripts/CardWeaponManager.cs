using System.Collections.Generic;
using UnityEngine;
using Vuforia;

public class CardWeaponManager : MonoBehaviour
{

    [Header("Prefabs")]
    public GameObject swordPrefab;
    public GameObject axePrefab;
    public GameObject playerPrefab;

    [Header("Players")]
    // Extra rotation so the character faces the arrow printed on the card
    public float playerFacingYaw = 0f;
    // Character height as a multiple of the card's longest side
    public float playerHeightPerCardSize = 1.5f;
    // Idle animation applied to the characters (humanoid)
    public RuntimeAnimatorController playerAnimator;

    [Header("Equipping (weapon card next to a player card)")]
    // Max distance between card centers to equip, in card lengths (1 = cards touching end to end)
    public float equipDistanceInCards = 1.3f;
    // Time the cards must stay close before the weapon is equipped
    public float equipTime = 0.8f;
    // Weapon height in the hand, as a fraction of the character height
    public float equippedWeaponHeight = 0.45f;
    // Weapon pose relative to the hand bone (tweak in Play mode)
    public Vector3 gripRotation = new Vector3(0f, 0f, 90f);
    public Vector3 gripOffset = Vector3.zero;
    // Material of the progress ring drawn around the player card
    public Material equipRingMaterial;

    [Header("Size (relative to the detected card)")]
    // Weapon height as a multiple of the card's longest side
    public float weaponHeightPerCardSize = 1.5f;
    public float heightAboveCard = 0.005f; // fraction of card size kept above the card surface

    [Header("Stability")]
    // Higher = follows the card faster, lower = smoother
    public float positionSmoothing = 12f;
    public float rotationSmoothing = 10f;
    public float scaleSmoothing = 8f;
    // How long to keep showing the weapon after the card stops being fully tracked
    public float lostTrackingGraceTime = 0.4f;

    [Header("Debug")]
    // Shows AR session and card tracking state on screen
    public bool showDebugInfo = true;

    private class SpawnedWeapon
    {
        public GameObject instance;
        public ImageTargetBehaviour image;
        public float baseHeight;     // prefab height at scale 1
        public bool isPlayer;
        public float lastTrackedTime;
        public bool placed;
        public GameObject prefab;
        public bool visible;
        public PlayerAvatar avatar;          // players only
        public SpawnedWeapon equippedTo;     // weapons only: player holding it
        public SpawnedWeapon equipTarget;    // weapons only: player it is being equipped to
        public float equipProgress;
        public LineRenderer ring;
    }

    private readonly Dictionary<int, SpawnedWeapon> spawnedWeapons = new Dictionary<int, SpawnedWeapon>();
    private readonly Dictionary<GameObject, float> prefabHeights = new Dictionary<GameObject, float>();

    void Awake()
    {
        // Shares the Vuforia camera with the red tip tracker.
        if (GetComponent<RedTipAttackTracker>() == null)
            gameObject.AddComponent<RedTipAttackTracker>();
    }

    public PlayerAvatar GetAttackPlayer(int playerIndex)
    {
        foreach (var player in spawnedWeapons.Values)
            if (player.isPlayer && player.visible && player.avatar != null &&
                player.avatar.isActiveAndEnabled && player.avatar.playerIndex == playerIndex && player.avatar.HasWeapon)
                return player.avatar;
        return null;
    }

    public void RegisterTarget(ImageTargetBehaviour target)
    {
        if (target != null && !spawnedWeapons.ContainsKey(target.GetInstanceID()))
            TrySpawnWeapon(target);
    }

    void OnDestroy()
    {
        foreach (var weapon in spawnedWeapons.Values)
        {
            if (weapon.instance != null) Destroy(weapon.instance);
            if (weapon.ring != null) Destroy(weapon.ring.gameObject);
        }
    }

    private void TrySpawnWeapon(ImageTargetBehaviour trackedImage)
    {
        GameObject prefab = GetPrefabFor(trackedImage.TargetName);
        if (prefab == null)
        {
            return;
        }

        // Not parented to the tracked image, so we can smooth its pose every frame
        GameObject instance = Instantiate(prefab);
        instance.SetActive(false);

        if (prefab == playerPrefab)
        {
            // player1 -> index 0, player2 -> index 1
            var avatar = instance.AddComponent<PlayerAvatar>();
            avatar.playerIndex = trackedImage.TargetName.Contains("2") ? 1 : 0;
            avatar.animatorController = playerAnimator;
        }

        spawnedWeapons[trackedImage.GetInstanceID()] = new SpawnedWeapon
        {
            instance = instance,
            image = trackedImage,
            baseHeight = GetPrefabHeight(prefab),
            isPlayer = prefab == playerPrefab,
            prefab = prefab,
            avatar = instance.GetComponent<PlayerAvatar>(),
            lastTrackedTime = -1f,
            placed = false
        };
    }

    private GameObject GetPrefabFor(string imageName)
    {
        string name = imageName.ToLower();
        if (name.Contains("sword") || name.Contains("espada"))
        {
            return swordPrefab;
        }
        if (name.Contains("axe") || name.Contains("machado"))
        {
            return axePrefab;
        }
        if (name.Contains("player") || name.Contains("jogador"))
        {
            return playerPrefab;
        }
        return null;
    }

    // Measures the prefab's rendered height once, so it can be scaled to the card size
    private float GetPrefabHeight(GameObject prefab)
    {
        if (prefabHeights.TryGetValue(prefab, out float height))
        {
            return height;
        }

        GameObject probe = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        height = 0f;
        var renderers = probe.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }
            height = bounds.size.y;
        }
        Destroy(probe);

        if (height <= 0f)
        {
            height = 1f;
        }
        prefabHeights[prefab] = height;
        return height;
    }

    void Update()
    {
        float now = Time.time;

        foreach (var weapon in spawnedWeapons.Values)
        {
            ImageTargetBehaviour image = weapon.image;
            if (image == null || weapon.instance == null)
            {
                continue;
            }

            // Moving cards require direct observation, not an extended last-known pose.
            if (image.TargetStatus.Status == Status.TRACKED)
            {
                weapon.lastTrackedTime = now;
            }

            bool visible = weapon.lastTrackedTime >= 0f && now - weapon.lastTrackedTime <= lostTrackingGraceTime;
            weapon.visible = visible;
            // An equipped weapon lives in the player's hand, not on its card
            bool showModel = visible && (weapon.isPlayer || weapon.equippedTo == null);
            if (!showModel)
            {
                if (weapon.instance.activeSelf)
                {
                    weapon.instance.SetActive(false);
                }
                // Snap back onto the card when it is found again
                weapon.placed = false;
                continue;
            }

            float cardSize = Mathf.Max(image.GetSize().x, image.GetSize().y);
            if (cardSize <= 0f)
            {
                continue;
            }

            Transform imageTransform = image.transform;
            Vector3 targetPosition = imageTransform.position + imageTransform.up * (cardSize * heightAboveCard);
            Quaternion targetRotation = imageTransform.rotation;
            float heightPerCard = weaponHeightPerCardSize;
            if (weapon.isPlayer)
            {
                targetRotation *= Quaternion.Euler(0f, playerFacingYaw, 0f);
                heightPerCard = playerHeightPerCardSize;
            }
            float targetScale = cardSize * heightPerCard / weapon.baseHeight;

            Transform t = weapon.instance.transform;
            if (!weapon.placed)
            {
                // Snap to the card the first time, then grow in
                t.SetPositionAndRotation(targetPosition, targetRotation);
                t.localScale = Vector3.zero;
                weapon.placed = true;
            }

            if (!weapon.instance.activeSelf)
            {
                weapon.instance.SetActive(true);
            }

            float dt = Time.deltaTime;
            t.position = Vector3.Lerp(t.position, targetPosition, 1f - Mathf.Exp(-positionSmoothing * dt));
            t.rotation = Quaternion.Slerp(t.rotation, targetRotation, 1f - Mathf.Exp(-rotationSmoothing * dt));
            t.localScale = Vector3.Lerp(t.localScale, Vector3.one * targetScale, 1f - Mathf.Exp(-scaleSmoothing * dt));
        }

        UpdateEquipping();
    }

    private static float CardSize(ImageTargetBehaviour image)
    {
        return Mathf.Max(image.GetSize().x, image.GetSize().y);
    }

    // Equips a weapon to the closest player whose card stays next to the weapon card
    private void UpdateEquipping()
    {
        float dt = Time.deltaTime;

        foreach (var weapon in spawnedWeapons.Values)
        {
            if (weapon.isPlayer)
            {
                continue;
            }

            SpawnedWeapon closest = null;
            if (weapon.visible && weapon.image != null)
            {
                float best = float.MaxValue;
                foreach (var player in spawnedWeapons.Values)
                {
                    if (!player.isPlayer || !player.visible || player.avatar == null || player.image == null)
                    {
                        continue;
                    }
                    float maxDistance = equipDistanceInCards * Mathf.Max(CardSize(weapon.image), CardSize(player.image));
                    float distance = Vector3.Distance(weapon.image.transform.position, player.image.transform.position);
                    if (distance < maxDistance && distance < best)
                    {
                        best = distance;
                        closest = player;
                    }
                }
            }

            // Already in this player's hand: nothing to do
            if (closest != null && closest == weapon.equippedTo)
            {
                closest = null;
            }

            if (closest != weapon.equipTarget)
            {
                weapon.equipTarget = closest;
                weapon.equipProgress = 0f;
            }

            if (closest == null)
            {
                SetRing(weapon, null, 0f);
                continue;
            }

            weapon.equipProgress += dt;
            float progress = Mathf.Clamp01(weapon.equipProgress / Mathf.Max(0.01f, equipTime));
            SetRing(weapon, closest, progress);

            if (progress >= 1f)
            {
                Equip(weapon, closest);
                weapon.equipTarget = null;
                weapon.equipProgress = 0f;
                SetRing(weapon, null, 0f);
            }
        }
    }

    private void Equip(SpawnedWeapon weapon, SpawnedWeapon player)
    {
        // The weapon leaves its previous owner
        if (weapon.equippedTo != null && weapon.equippedTo.avatar != null)
        {
            weapon.equippedTo.avatar.Unequip();
        }

        // The weapon this player was holding goes back to its card
        foreach (var other in spawnedWeapons.Values)
        {
            if (other != weapon && other.equippedTo == player)
            {
                other.equippedTo = null;
            }
        }

        player.avatar.Equip(weapon.prefab, weapon.baseHeight, equippedWeaponHeight, gripOffset, Quaternion.Euler(gripRotation));
        player.avatar.attackTriggerPrefix = weapon.prefab == swordPrefab ? "SwordAttack" : "AxeAttack";
        weapon.equippedTo = player;
    }

    private void SetRing(SpawnedWeapon weapon, SpawnedWeapon player, float progress)
    {
        if (player == null || progress <= 0f)
        {
            if (weapon.ring != null)
            {
                weapon.ring.enabled = false;
            }
            return;
        }

        if (weapon.ring == null)
        {
            var ringObject = new GameObject("Equip Ring");
            weapon.ring = ringObject.AddComponent<LineRenderer>();
            weapon.ring.useWorldSpace = true;
            weapon.ring.numCapVertices = 4;
            weapon.ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (equipRingMaterial != null)
            {
                weapon.ring.sharedMaterial = equipRingMaterial;
            }
        }

        // Arc around the player card that fills up while the cards stay close
        Transform card = player.image.transform;
        float cardSize = CardSize(player.image);
        float radius = cardSize * 0.7f;
        int segments = Mathf.Max(2, Mathf.CeilToInt(64 * progress));
        weapon.ring.enabled = true;
        weapon.ring.widthMultiplier = cardSize * 0.06f;
        weapon.ring.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            float angle = progress * Mathf.PI * 2f * i / segments;
            Vector3 local = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            weapon.ring.SetPosition(i, card.position + card.rotation * local + card.up * (cardSize * 0.02f));
        }
    }

    void OnGUI()
    {
        if (!showDebugInfo)
        {
            return;
        }

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("Vuforia - cartoes: " + spawnedWeapons.Count);
        foreach (var weapon in spawnedWeapons.Values)
        {
            var image = weapon.image;
            if (image == null) continue;
            Vector2 size = image.GetSize() * 100f;
            lines.AppendLine($"{image.TargetName}: {image.TargetStatus.Status} ({image.TargetStatus.StatusInfo})  {size.x:F1} x {size.y:F1} cm");
        }

        var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(24, Screen.height / 50) };
        style.normal.textColor = Color.yellow;
        GUI.Label(new Rect(30, 80, Screen.width - 60, Screen.height / 3f), lines.ToString(), style);
    }
}
