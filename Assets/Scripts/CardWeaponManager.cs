using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class CardWeaponManager : MonoBehaviour
{
    public ARTrackedImageManager trackedImageManager;

    [Header("Prefabs")]
    public GameObject swordPrefab;
    public GameObject axePrefab;
    public GameObject playerPrefab;

    [Header("Players")]
    // Extra rotation so the character faces the arrow printed on the card
    public float playerFacingYaw = 0f;
    // Character height as a multiple of the card's longest side
    public float playerHeightPerCardSize = 1.5f;

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
        public ARTrackedImage image;
        public float baseHeight;     // prefab height at scale 1
        public bool isPlayer;
        public float lastTrackedTime;
        public bool placed;
    }

    private readonly Dictionary<TrackableId, SpawnedWeapon> spawnedWeapons = new Dictionary<TrackableId, SpawnedWeapon>();
    private readonly Dictionary<GameObject, float> prefabHeights = new Dictionary<GameObject, float>();

    void OnEnable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }
    }

    void OnDisable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        foreach (var trackedImage in eventArgs.added)
        {
            TrySpawnWeapon(trackedImage);
        }

        foreach (var trackedImage in eventArgs.updated)
        {
            if (!spawnedWeapons.ContainsKey(trackedImage.trackableId))
            {
                TrySpawnWeapon(trackedImage);
            }
        }

        foreach (var removed in eventArgs.removed)
        {
            if (spawnedWeapons.TryGetValue(removed.Key, out var weapon))
            {
                Destroy(weapon.instance);
                spawnedWeapons.Remove(removed.Key);
            }
        }
    }

    private void TrySpawnWeapon(ARTrackedImage trackedImage)
    {
        GameObject prefab = GetPrefabFor(trackedImage.referenceImage.name);
        if (prefab == null)
        {
            return;
        }

        // Not parented to the tracked image, so we can smooth its pose every frame
        GameObject instance = Instantiate(prefab);
        instance.SetActive(false);

        spawnedWeapons[trackedImage.trackableId] = new SpawnedWeapon
        {
            instance = instance,
            image = trackedImage,
            baseHeight = GetPrefabHeight(prefab),
            isPlayer = prefab == playerPrefab,
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
            ARTrackedImage image = weapon.image;
            if (image == null || weapon.instance == null)
            {
                continue;
            }

            // Limited on ARCore means "last known pose": the card isn't actually being seen
            if (image.trackingState == TrackingState.Tracking)
            {
                weapon.lastTrackedTime = now;
            }

            bool visible = weapon.lastTrackedTime >= 0f && now - weapon.lastTrackedTime <= lostTrackingGraceTime;
            if (!visible)
            {
                if (weapon.instance.activeSelf)
                {
                    weapon.instance.SetActive(false);
                }
                // Snap back onto the card when it is found again
                weapon.placed = false;
                continue;
            }

            float cardSize = Mathf.Max(image.size.x, image.size.y);
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
    }

    void OnGUI()
    {
        if (!showDebugInfo || trackedImageManager == null)
        {
            return;
        }

        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"AR Session: {ARSession.state} ({ARSession.notTrackingReason})");
        var library = trackedImageManager.referenceLibrary;
        lines.AppendLine($"Cards in library: {(library != null ? library.count : 0)}");
        foreach (var image in trackedImageManager.trackables)
        {
            Vector2 size = image.size * 100f;
            lines.AppendLine($"{image.referenceImage.name}: {image.trackingState}  {size.x:F1} x {size.y:F1} cm");
        }

        var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(24, Screen.height / 50) };
        style.normal.textColor = Color.yellow;
        GUI.Label(new Rect(30, 80, Screen.width - 60, Screen.height / 3f), lines.ToString(), style);
    }
}
