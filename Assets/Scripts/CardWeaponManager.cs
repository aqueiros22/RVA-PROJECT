using System.Collections.Generic;
using UnityEngine;
using Vuforia;

public class CardWeaponManager : MonoBehaviour
{

    [Header("Prefabs")]
    public GameObject swordPrefab;
    public GameObject axePrefab;
    public GameObject shieldPrefab;
    public GameObject daggerPrefab;
    public GameObject hammerPrefab;
    public GameObject playerPrefab;

    [Header("Prefabs of the II cards (sword2, axe2…)")]
    public GameObject sword2Prefab;
    public GameObject axe2Prefab;
    public GameObject shield2Prefab;
    public GameObject dagger2Prefab;
    public GameObject hammer2Prefab;

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
    // Default weapon pose relative to the hand bone (tweak in Play mode);
    // a weapon prefab with a WeaponGrip component uses its own pose instead
    public Vector3 gripRotation = new Vector3(0f, 0f, 90f);
    public Vector3 gripOffset = Vector3.zero;
    // Material of the progress ring drawn around the player card
    public Material equipRingMaterial;

    [Header("Class icons (shown over weapons with WeaponStats)")]
    public Sprite lightIcon;
    public Sprite heavyIcon;
    public Sprite defenseIcon;
    // Material using the Arena/ClassIcon shader, which tints the black symbols
    public Material classIconMaterial;

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

    [Header("Duel stability (player cards on the table)")]
    // A tracked pose further than this from the character (in card lengths, or degrees) is a glitch...
    public float jumpTolerance = 0.3f;
    public float jumpAngle = 25f;
    // ...unless it lasts this long: then the card really moved
    public float jumpAcceptTime = 0.5f;
    // Multiplies the smoothing while the duel is on (lower = steadier)
    [Range(0.1f, 1f)] public float holdSmoothing = 0.5f;

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
        // Players during the duel: this frame's goal pose, and whether it can be trusted
        public bool holdGood;
        public Vector3 holdPosition;
        public Quaternion holdRotation;
        public float holdScale;
        public float jumpSince = -1f;
    }

    private readonly Dictionary<int, SpawnedWeapon> spawnedWeapons = new Dictionary<int, SpawnedWeapon>();
    private readonly List<SpawnedWeapon> heldPlayers = new List<SpawnedWeapon>();
    // Player B's pose in player A's card frame, while both cards are seen during the duel
    private bool hasRelative;
    private Vector3 relativePosition;
    private Quaternion relativeRotation = Quaternion.identity;
    private readonly Dictionary<GameObject, float> prefabHeights = new Dictionary<GameObject, float>();

    private PlayerSetupUI playerSetup;
    public DuelController Duel { get; private set; }

    void Awake()
    {
        if (GetComponent<ArenaMenu>() == null) gameObject.AddComponent<ArenaMenu>();
        playerSetup = FindAnyObjectByType<PlayerSetupUI>();
        Duel = GetComponent<DuelController>();
        if (Duel == null) Duel = gameObject.AddComponent<DuelController>();
        // Shares the Vuforia camera with the gesture tracker.
        if (GetComponent<ColorGestureTracker>() == null)
            gameObject.AddComponent<ColorGestureTracker>();
    }

    public bool SetupInProgress
    {
        get
        {
            return ArenaMenu.IsOpen || (playerSetup != null && playerSetup.isActiveAndEnabled && !playerSetup.IsComplete);
        }
    }

    public bool IsPlayerTracked(int playerIndex)
    {
        foreach (var player in spawnedWeapons.Values)
            if (player.isPlayer && player.avatar != null && player.avatar.playerIndex == playerIndex &&
                player.image != null && player.image.TargetStatus.Status == Status.TRACKED)
                return true;
        return false;
    }

    public PlayerAvatar GetAttackPlayer(int playerIndex)
    {
        if (SetupInProgress) return null;
        foreach (var player in spawnedWeapons.Values)
            if (player.isPlayer && player.visible && player.avatar != null &&
                player.avatar.isActiveAndEnabled && player.avatar.playerIndex == playerIndex && player.avatar.HasWeapon)
                return player.avatar;
        return null;
    }

    // A visible player, with or without a weapon
    public PlayerAvatar GetPlayer(int playerIndex)
    {
        if (SetupInProgress) return null;
        foreach (var player in spawnedWeapons.Values)
            if (player.isPlayer && player.visible && player.avatar != null &&
                player.avatar.isActiveAndEnabled && player.avatar.playerIndex == playerIndex)
                return player.avatar;
        return null;
    }

    // Includes temporarily untracked avatars, so health/death/restarts persist.
    public PlayerAvatar GetAvatar(int playerIndex)
    {
        foreach (var player in spawnedWeapons.Values)
            if (player.isPlayer && player.avatar != null && player.avatar.playerIndex == playerIndex)
                return player.avatar;
        return null;
    }

    public void ClearRoundWeapons()
    {
        foreach (var item in spawnedWeapons.Values)
        {
            if (item.avatar != null) item.avatar.Unequip();
            item.equippedTo = null;
            item.equipTarget = null;
            item.equipProgress = 0;
            SetRing(item, null, 0);
        }
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

        float baseHeight = GetPrefabHeight(prefab);
        var stats = instance.GetComponent<WeaponStats>();
        if (stats != null && lightIcon != null && heavyIcon != null && defenseIcon != null && classIconMaterial != null)
        {
            // Indexed by WeaponClass
            var sprites = new[] { lightIcon, heavyIcon, defenseIcon };
            instance.AddComponent<WeaponIcons>().Setup(stats, baseHeight, sprites, classIconMaterial);
        }

        spawnedWeapons[trackedImage.GetInstanceID()] = new SpawnedWeapon
        {
            instance = instance,
            image = trackedImage,
            baseHeight = baseHeight,
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
        if (name.Contains("player") || name.Contains("jogador"))
        {
            return playerPrefab;
        }
        // Weapon cards ending in "2" (sword2, shield2…) are the II cards: same weapon, another model
        bool second = name.EndsWith("2");
        if (name.Contains("sword") || name.Contains("espada"))
        {
            return second ? sword2Prefab : swordPrefab;
        }
        if (name.Contains("axe") || name.Contains("machado"))
        {
            return second ? axe2Prefab : axePrefab;
        }
        if (name.Contains("shield") || name.Contains("escudo"))
        {
            return second ? shield2Prefab : shieldPrefab;
        }
        if (name.Contains("dagger") || name.Contains("adaga"))
        {
            return second ? dagger2Prefab : daggerPrefab;
        }
        if (name.Contains("hammer") || name.Contains("martelo"))
        {
            return second ? hammer2Prefab : hammerPrefab;
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
        bool hold = Duel != null && Duel.HoldsCards;
        heldPlayers.Clear();
        if (!hold) hasRelative = false;

        foreach (var weapon in spawnedWeapons.Values)
        {
            ImageTargetBehaviour image = weapon.image;
            if (image == null || weapon.instance == null)
            {
                continue;
            }

            // Moving cards require direct observation, not an extended last-known pose.
            bool tracked = image.TargetStatus.Status == Status.TRACKED;
            if (tracked)
            {
                weapon.lastTrackedTime = now;
            }

            bool recent = weapon.lastTrackedTime >= 0f && now - weapon.lastTrackedTime <= lostTrackingGraceTime;
            // Once the cards are down (recording, fight), a hand or the pen covering a player card
            // must not make the character vanish or jump: see UpdateHeldPlayers
            bool held = weapon.isPlayer && weapon.placed && hold;
            bool visible = recent || held;
            weapon.visible = visible;
            // An equipped weapon lives in the player's hand, not on its card
            bool showModel = !SetupInProgress && visible && (weapon.isPlayer || (weapon.equippedTo == null && !Duel.IsUsed(image.TargetName)));
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
            if (held)
            {
                if (!weapon.instance.activeSelf) weapon.instance.SetActive(true);
                heldPlayers.Add(weapon);
                weapon.holdGood = false;
                if (!tracked || cardSize <= 0f)
                {
                    weapon.jumpSince = -1f;
                    continue;
                }
            }
            else if (cardSize <= 0f)
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
            if (held)
            {
                // A hand half over the card makes the tracked pose leap: trust a big change only once it lasts
                bool jump = Vector3.Distance(targetPosition, t.position) > jumpTolerance * cardSize ||
                            Quaternion.Angle(targetRotation, t.rotation) > jumpAngle;
                if (!jump) weapon.jumpSince = -1f;
                else if (weapon.jumpSince < 0f) weapon.jumpSince = now;
                weapon.holdGood = !jump || now - weapon.jumpSince >= jumpAcceptTime;
                weapon.holdPosition = targetPosition;
                weapon.holdRotation = targetRotation;
                weapon.holdScale = targetScale;
                continue;
            }
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

        if (hold) UpdateHeldPlayers();
        if (!SetupInProgress) UpdateEquipping();
    }

    // During the duel the two player cards stay put on the table. A player whose card is clearly
    // tracked follows it; one that is covered or jumping is placed from the other card, keeping the
    // spacing last seen between them; if both are covered, both stay where they are.
    private void UpdateHeldPlayers()
    {
        SpawnedWeapon a = null, b = null;
        foreach (var player in heldPlayers)
        {
            if (player.avatar != null && player.avatar.playerIndex == 1) b = player;
            else a = player;
        }

        if (a != null && b != null && a.holdGood && b.holdGood)
        {
            Quaternion inverse = Quaternion.Inverse(a.holdRotation);
            Vector3 position = inverse * (b.holdPosition - a.holdPosition);
            Quaternion rotation = inverse * b.holdRotation;
            // Averaged over a few frames, so one noisy frame does not bend the spacing
            relativePosition = hasRelative ? Vector3.Lerp(relativePosition, position, 0.2f) : position;
            relativeRotation = hasRelative ? Quaternion.Slerp(relativeRotation, rotation, 0.2f) : rotation;
            hasRelative = true;
        }
        if (hasRelative && a != null && b != null)
        {
            if (!a.holdGood && b.holdGood)
            {
                a.holdRotation = b.holdRotation * Quaternion.Inverse(relativeRotation);
                a.holdPosition = b.holdPosition - a.holdRotation * relativePosition;
                a.holdGood = true;
            }
            else if (!b.holdGood && a.holdGood)
            {
                b.holdPosition = a.holdPosition + a.holdRotation * relativePosition;
                b.holdRotation = a.holdRotation * relativeRotation;
                b.holdGood = true;
            }
        }

        float dt = Time.deltaTime;
        foreach (var player in heldPlayers)
        {
            if (!player.holdGood) continue;
            Transform t = player.instance.transform;
            t.position = Vector3.Lerp(t.position, player.holdPosition, 1f - Mathf.Exp(-positionSmoothing * holdSmoothing * dt));
            t.rotation = Quaternion.Slerp(t.rotation, player.holdRotation, 1f - Mathf.Exp(-rotationSmoothing * holdSmoothing * dt));
            if (player.holdScale > 0f)
                t.localScale = Vector3.Lerp(t.localScale, Vector3.one * player.holdScale, 1f - Mathf.Exp(-scaleSmoothing * dt));
        }
    }

    private static float CardSize(ImageTargetBehaviour image)
    {
        return Mathf.Max(image.GetSize().x, image.GetSize().y);
    }

    // Equips a weapon to the closest player whose card stays next to the weapon card
    private void UpdateEquipping()
    {
        if (Time.timeScale == 0f) return;
        float dt = Time.deltaTime;

        foreach (var weapon in spawnedWeapons.Values)
        {
            if (weapon.isPlayer) continue;
            int owner = weapon.image != null && weapon.image.TargetName.EndsWith("2") ? 1 : 0;
            if (ArenaMenu.IsComputer(owner)) continue;
            if (weapon.image == null || !Duel.CanEquip(owner, weapon.image.TargetName))
            {
                weapon.equipTarget = null;
                weapon.equipProgress = 0;
                SetRing(weapon, null, 0);
                continue;
            }

            SpawnedWeapon closest = null;
            if (weapon.visible && weapon.image != null)
            {
                float best = float.MaxValue;
                foreach (var player in spawnedWeapons.Values)
                {
                    if (!player.isPlayer || !player.visible || player.avatar == null || player.image == null || player.avatar.playerIndex != owner)
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

    // Computer weapons use the same card IDs/rules, but do not need physical weapon cards.
    public bool EquipRandomComputerWeapon(int playerIndex)
    {
        var player = GetPlayer(playerIndex);
        if (player == null || Duel.Match == null) return false;
        var prefabs = playerIndex == 0
            ? new[] { swordPrefab, axePrefab, shieldPrefab, daggerPrefab, hammerPrefab }
            : new[] { sword2Prefab, axe2Prefab, shield2Prefab, dagger2Prefab, hammer2Prefab };
        var names = new[] { "sword", "axe", "shield", "dagger", "hammer" };
        var available = new List<int>();
        for (int i = 0; i < prefabs.Length; i++)
        {
            string id = names[i] + (playerIndex == 1 ? "2" : "");
            if (prefabs[i] != null && prefabs[i].GetComponent<WeaponStats>() != null && Duel.CanEquip(playerIndex, id))
                available.Add(i);
        }
        if (available.Count == 0) return false;
        int choice = available[Random.Range(0, available.Count)];
        var prefab = prefabs[choice];
        var stats = prefab.GetComponent<WeaponStats>();
        if (!Duel.Equip(playerIndex, names[choice] + (playerIndex == 1 ? "2" : ""), stats)) return false;
        var grip = prefab.GetComponent<WeaponGrip>();
        player.Equip(prefab, GetPrefabHeight(prefab), equippedWeaponHeight,
            grip != null ? grip.offset : gripOffset, Quaternion.Euler(grip != null ? grip.rotation : gripRotation));
        GameAudio.Play("equip");
        player.attackSpeed = stats.weaponClass == WeaponClass.Heavy ? 0.8f : 1f;
        return true;
    }

    private void Equip(SpawnedWeapon weapon, SpawnedWeapon player)
    {
        var weaponStats = weapon.prefab.GetComponent<WeaponStats>();
        if (!Duel.Equip(player.avatar.playerIndex, weapon.image.TargetName, weaponStats)) return;
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

        var grip = weapon.prefab.GetComponent<WeaponGrip>();
        Vector3 offset = grip != null ? grip.offset : gripOffset;
        Vector3 rotation = grip != null ? grip.rotation : gripRotation;
        player.avatar.Equip(weapon.prefab, weapon.baseHeight, equippedWeaponHeight, offset, Quaternion.Euler(rotation));
        GameAudio.Play("equip");
        // Heavy weapons swing slower
        var stats = weapon.prefab.GetComponent<WeaponStats>();
        bool heavy = stats != null && stats.weaponClass == WeaponClass.Heavy;
        player.avatar.attackSpeed = heavy ? 0.8f : 1f;
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
        if (Duel != null || SetupInProgress || !showDebugInfo)
        {
            return;
        }

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("Vuforia - cards: " + spawnedWeapons.Count);
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
