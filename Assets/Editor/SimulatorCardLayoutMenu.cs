using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Creates the "Simulator Cards" layout used to test with the Vuforia Simulator
public static class SimulatorCardLayoutMenu
{
    private static readonly string[] CardNames = { "sword", "player1", "player2", "axe" };
    // Card size in meters, as printed (matches the Vuforia database)
    private static readonly Vector2 CardSize = new Vector2(0.063f, 0.088f);
    // Far enough apart that nothing is equipped until a card is dragged closer
    private const float Spacing = 0.15f;

    [MenuItem("Arena/Criar cartoes do simulador")]
    static void Create()
    {
        foreach (var old in Object.FindObjectsByType<SimulatorCardLayout>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        var setup = Object.FindFirstObjectByType<VuforiaArenaSetup>();
        Camera cam = setup != null && setup.arCamera != null ? setup.arCamera : Camera.main;
        Vector3 origin = cam != null ? cam.transform.position : Vector3.zero;
        Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();

        var root = new GameObject("Simulator Cards");
        root.tag = "EditorOnly";
        root.transform.SetPositionAndRotation(origin + forward * 0.4f + Vector3.down * 0.3f, Quaternion.LookRotation(forward, Vector3.up));
        root.AddComponent<SimulatorCardLayout>();

        for (int i = 0; i < CardNames.Length; i++)
        {
            var card = new GameObject(CardNames[i]);
            card.tag = "EditorOnly";
            card.transform.SetParent(root.transform, false);
            card.transform.localPosition = new Vector3((i - (CardNames.Length - 1) * 0.5f) * Spacing, 0f, 0f);

            // Quads face -Z; lay it flat so the card's normal is +Y like an image target
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Card";
            quad.tag = "EditorOnly";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(card.transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(CardSize.x, CardSize.y, 1f);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Images/Markers/Materials/" + CardNames[i] + ".mat");
            if (material != null)
            {
                quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        Undo.RegisterCreatedObjectUndo(root, "Criar cartoes do simulador");
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
    }
}
