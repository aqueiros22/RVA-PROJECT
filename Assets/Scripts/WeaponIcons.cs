using UnityEngine;

// Class icons floating around a weapon on its card, always facing the camera:
// its own class below the weapon, the class it beats (green) and the one that beats it (red) above.
// Added by CardWeaponManager to the weapon shown on the card; hidden with it once equipped.
public class WeaponIcons : MonoBehaviour
{
    private static readonly Color OwnColor = Color.white;
    private static readonly Color StrongColor = new Color(0.25f, 0.9f, 0.35f);
    private static readonly Color WeakColor = new Color(0.95f, 0.25f, 0.25f);
    // Dark enough for the white icon to read on top of the white card
    private static readonly Color BackdropColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);
    private static Sprite backdropSprite;

    // Icon size and spacing, as fractions of the weapon height
    private const float IconSize = 0.32f;
    private const float AboveGap = 0.22f;
    private const float BelowDrop = 0.05f;

    private float weaponHeight;
    private Transform own, strong, weak;
    private Camera mainCamera;

    // weaponHeight: the weapon's height at scale 1, so the icons follow its size on the card
    public void Setup(WeaponStats stats, float weaponHeight, Sprite[] classSprites, Material material)
    {
        this.weaponHeight = weaponHeight;
        own = CreateIcon("Own Class Icon", classSprites[(int)stats.weaponClass], OwnColor, material);
        strong = CreateIcon("Strong Against Icon", classSprites[(int)WeaponStats.StrongAgainst(stats.weaponClass)], StrongColor, material);
        weak = CreateIcon("Weak Against Icon", classSprites[(int)WeaponStats.WeakAgainst(stats.weaponClass)], WeakColor, material);
    }

    private Transform CreateIcon(string iconName, Sprite sprite, Color color, Material material)
    {
        var icon = new GameObject(iconName).transform;
        icon.SetParent(transform, false);

        // Dark disc behind the symbol so it reads on any background
        var backdrop = new GameObject("Backdrop").AddComponent<SpriteRenderer>();
        backdrop.transform.SetParent(icon, false);
        backdrop.sprite = GetBackdropSprite();
        backdrop.sharedMaterial = material;
        Tint(backdrop, BackdropColor);
        backdrop.sortingOrder = 10;

        var symbol = new GameObject("Symbol").AddComponent<SpriteRenderer>();
        symbol.transform.SetParent(icon, false);
        symbol.transform.localScale = Vector3.one * 0.72f;
        symbol.sprite = sprite;
        symbol.sharedMaterial = material;
        Tint(symbol, color);
        symbol.sortingOrder = 11;
        return icon;
    }

    // The ClassIcon shader reads its colour from _Color, set per renderer
    private static void Tint(SpriteRenderer renderer, Color color)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }

    void LateUpdate()
    {
        if (own == null)
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

        // World size of the weapon right now (it grows in when the card is found)
        float height = weaponHeight * transform.lossyScale.y;
        float size = height * IconSize;
        Vector3 up = transform.up;
        Vector3 right = mainCamera.transform.right;
        Vector3 basePosition = transform.position;

        Place(own, basePosition - up * (height * BelowDrop), size);
        Vector3 top = basePosition + up * (height * (1f + AboveGap));
        Place(strong, top - right * (size * 0.6f), size);
        Place(weak, top + right * (size * 0.6f), size);
    }

    private void Place(Transform icon, Vector3 position, float worldSize)
    {
        icon.position = position;
        icon.rotation = mainCamera.transform.rotation;
        Vector3 parentScale = transform.lossyScale;
        // The sprites are 1 unit wide at scale 1 (pixels per unit = texture width)
        icon.localScale = new Vector3(worldSize / Mathf.Max(0.0001f, parentScale.x),
                                      worldSize / Mathf.Max(0.0001f, parentScale.y),
                                      worldSize / Mathf.Max(0.0001f, parentScale.z));
    }

    // A soft-edged white circle, made once and tinted per icon
    private static Sprite GetBackdropSprite()
    {
        if (backdropSprite != null)
        {
            return backdropSprite;
        }
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float radius = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance)));
            }
        }
        texture.Apply();
        backdropSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return backdropSprite;
    }
}
