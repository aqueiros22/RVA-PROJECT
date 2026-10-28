using UnityEngine;

// Procedural lightning follows the equipped weapon, including its attack animation.
public class WarCryAura : MonoBehaviour
{
    public float Power;
    private LineRenderer bolt;
    private Material material;
    private Bounds bounds;
    private float nextFlicker;

    void Start()
    {
        bounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);
        bool first = true;
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            var world = renderer.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = world.center + Vector3.Scale(world.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var point = transform.InverseTransformPoint(corner);
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        }
        var go = new GameObject("War Cry Lightning");
        go.transform.SetParent(transform, false);
        bolt = go.AddComponent<LineRenderer>();
        bolt.useWorldSpace = false;
        bolt.positionCount = 32;
        bolt.numCapVertices = 3;
        bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bolt.receiveShadows = false;
        var shader = Resources.Load<Shader>("WarCryLightning");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) { bolt.enabled = false; enabled = false; return; }
        material = new Material(shader);
        bolt.sharedMaterial = material;
    }

    void Update()
    {
        if (bolt == null) return;
        bolt.enabled = Power > 0.01f;
        if (!bolt.enabled || Time.unscaledTime < nextFlicker) return;
        nextFlicker = Time.unscaledTime + 0.045f;
        var size = bounds.size;
        int axis = size.x > size.y ? 0 : 1;
        if (size.z > size[axis]) axis = 2;
        var along = Vector3.zero; along[axis] = 1;
        var across = axis == 0 ? Vector3.up : Vector3.right;
        var other = Vector3.Cross(along, across);
        float length = Mathf.Max(0.01f, size[axis]);
        float radius = length * Mathf.Lerp(0.035f, 0.12f, Power);
        bolt.widthMultiplier = length * Mathf.Lerp(0.007f, 0.025f, Power);
        bolt.startColor = Color.Lerp(new Color(0.2f, 0.6f, 1), Color.white, Power);
        bolt.endColor = new Color(0.3f, 0.65f, 1, 0.6f);
        for (int i = 0; i < bolt.positionCount; i++)
        {
            float t = i / (float)(bolt.positionCount - 1);
            float angle = t * Mathf.PI * 8 + Time.unscaledTime * 10;
            float jitter = radius * Random.Range(0.6f, 1.4f);
            bolt.SetPosition(i, bounds.center + along * ((t - 0.5f) * length) +
                (across * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * jitter);
        }
    }

    void OnDestroy() { if (material != null) Destroy(material); }
}
