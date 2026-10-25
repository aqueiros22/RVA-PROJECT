using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Creates the "Simulator Cards" layout used to test with the Vuforia Simulator.
// Running it again keeps the cards already laid out and only adds the missing ones.
public static class SimulatorCardLayoutMenu
{
    private static readonly string[] CardNames = { "sword", "player1", "player2", "axe", "shield", "dagger", "hammer", "shield2" };
    private const string MarkersFolder = "Assets/Images/Markers/";
    // Card size in meters, as printed (matches the Vuforia database)
    private static readonly Vector2 CardSize = new Vector2(0.063f, 0.088f);
    // Far enough apart that nothing is equipped until a card is dragged closer
    private const float Spacing = 0.15f;
    private const int CardsPerRow = 4;

    [MenuItem("Arena/Criar cartoes do simulador")]
    static void Create()
    {
        var layout = Object.FindFirstObjectByType<SimulatorCardLayout>(FindObjectsInactive.Include);
        GameObject root;
        if (layout != null)
        {
            root = layout.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(root, "Adicionar cartoes do simulador");
        }
        else
        {
            var setup = Object.FindFirstObjectByType<VuforiaArenaSetup>();
            Camera cam = setup != null && setup.arCamera != null ? setup.arCamera : Camera.main;
            Vector3 origin = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            root = new GameObject("Simulator Cards");
            root.tag = "EditorOnly";
            root.transform.SetPositionAndRotation(origin + forward * 0.4f + Vector3.down * 0.3f, Quaternion.LookRotation(forward, Vector3.up));
            root.AddComponent<SimulatorCardLayout>();
            Undo.RegisterCreatedObjectUndo(root, "Criar cartoes do simulador");
        }

        for (int i = 0; i < CardNames.Length; i++)
        {
            if (root.transform.Find(CardNames[i]) != null)
            {
                continue;
            }

            var card = new GameObject(CardNames[i]);
            card.tag = "EditorOnly";
            card.transform.SetParent(root.transform, false);
            int column = i % CardsPerRow, row = i / CardsPerRow;
            card.transform.localPosition = new Vector3((column - (CardsPerRow - 1) * 0.5f) * Spacing, 0f, -row * Spacing);

            // Quads face -Z; lay it flat so the card's normal is +Y like an image target
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Card";
            quad.tag = "EditorOnly";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(card.transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(CardSize.x, CardSize.y, 1f);
            var material = GetCardMaterial(CardNames[i]);
            if (material != null)
            {
                quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    // Marker material for a card, created from its PNG (like the existing ones) when missing
    private static Material GetCardMaterial(string cardName)
    {
        string path = MarkersFolder + "Materials/" + cardName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MarkersFolder + cardName + ".png");
        if (material != null || texture == null)
        {
            return material;
        }
        material = new Material(Shader.Find("Unlit/Texture")) { name = cardName, mainTexture = texture };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
