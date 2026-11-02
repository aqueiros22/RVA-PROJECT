using UnityEngine;

// The fighting stage shown between the two player cards: centred on them, turned so they stand on
// opposite sides, sized from the gap between the cards. The characters stand on its floor (FloorOffset).
// It rises out of the table when raised and sinks back into it when lowered.
public class ArenaStage : MonoBehaviour
{
    // Stage width as a multiple of the distance between the two player cards
    public float stageWidthPerGap = 2.6f;
    // Floor height in the model's own units; 0 measures it from the model
    public float floorHeightOverride = 0f;
    public float positionSmoothing = 8f;
    public float rotationSmoothing = 8f;
    public float scaleSmoothing = 6f;
    // Seconds to rise out of the table (and to sink back)
    public float riseSeconds = 1.1f;

    // Height of the floor above the table at the current scale (world units)
    public float FloorOffset => placed ? floorHeight * transform.localScale.y : 0f;
    // Fully risen / fully sunk into the table
    public bool Up => placed && rise >= 1f;
    public bool Down => !placed || rise <= 0f;

    private float baseWidth = 1f;
    private float floorHeight;
    private Vector3 pivotOffset;
    private bool placed;
    // 0 = sunk into the table, 1 = fully risen
    private float rise;
    private Vector3 center;
    private float width;

    void Awake()
    {
        Measure();
    }

    // Floor top and width at scale 1: the meshes under the middle of the stage, leaving out the
    // flags and the bell, which stand at the corners and reach far above the floor
    private void Measure()
    {
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        Vector3 scale = transform.localScale;
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;

        var renderers = GetComponentsInChildren<Renderer>(true);
        Bounds all = new Bounds();
        bool any = false;
        foreach (var r in renderers)
        {
            if (!any) all = r.bounds;
            else all.Encapsulate(r.bounds);
            any = true;
        }

        Bounds floor = new Bounds();
        bool found = false;
        foreach (var r in renderers)
        {
            if (IsDecoration(r)) continue;
            Bounds b = r.bounds;
            if (all.center.x < b.min.x || all.center.x > b.max.x || all.center.z < b.min.z || all.center.z > b.max.z)
                continue;
            if (!found) floor = b;
            else floor.Encapsulate(b);
            found = true;
        }
        if (!found) floor = all;

        floorHeight = floorHeightOverride > 0f ? floorHeightOverride : Mathf.Max(0f, floor.max.y - all.min.y);
        baseWidth = Mathf.Max(floor.size.x, floor.size.z, 1e-4f);
        // The model's pivot may be off the middle of the floor or above its bottom
        pivotOffset = new Vector3(floor.center.x, all.min.y, floor.center.z);

        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = scale;
    }

    private static bool IsDecoration(Renderer r)
    {
        foreach (var material in r.sharedMaterials)
        {
            if (material == null) continue;
            string name = material.name.ToLower();
            if (name.Contains("flag") || name.Contains("bell")) return true;
        }
        return false;
    }

    // a and b are the two player cards (on the table), up is the table's normal; raised says
    // whether the stage should be rising out of the table or sinking back into it
    public void Place(Vector3 a, Vector3 b, Vector3 up, bool raised)
    {
        Vector3 across = Vector3.ProjectOnPlane(b - a, up);
        float gap = across.magnitude;
        if (gap < 1e-4f) return;

        // The stage's X axis runs from player A to player B
        Quaternion targetRotation = Quaternion.LookRotation(Vector3.Cross(across / gap, up), up);
        float targetScale = gap * stageWidthPerGap / baseWidth;
        Vector3 targetCenter = (a + b) * 0.5f;

        float dt = Time.deltaTime;
        if (!placed)
        {
            // Snap to the cards the first time, flat on the table
            center = targetCenter;
            width = targetScale;
            transform.rotation = targetRotation;
            rise = 0f;
            placed = true;
        }
        center = Vector3.Lerp(center, targetCenter, 1f - Mathf.Exp(-positionSmoothing * dt));
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-rotationSmoothing * dt));
        width = Mathf.Lerp(width, targetScale, 1f - Mathf.Exp(-scaleSmoothing * dt));
        rise = Mathf.MoveTowards(rise, raised ? 1f : 0f, dt / Mathf.Max(riseSeconds, 0.01f));

        // Grows up from the table, overshooting a little before it settles
        Vector3 scale = new Vector3(width, width * Mathf.Max(0f, EaseOutBack(rise)), width);
        transform.localScale = scale;
        // Middle of the floor between the cards, bottom of the stage on the table
        transform.position = center - transform.rotation * Vector3.Scale(pivotOffset, scale);
    }

    private static float EaseOutBack(float t)
    {
        const float c = 1.4f;
        t -= 1f;
        return 1f + (c + 1f) * t * t * t + c * t * t;
    }

    public void Hide()
    {
        placed = false;
        rise = 0f;
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }
}
