using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vuforia;

// Editor-only helper for the Vuforia Simulator: each child card drives the simulated
// image target with the same name, so the cards can be laid out in the Scene view.
// The object is tagged EditorOnly and does nothing on a device or in Webcam mode.
// Runs before VuforiaArenaSetup so the simulator starts from the aimed camera pose.
[DefaultExecutionOrder(-2000)]
public class SimulatorCardLayout : MonoBehaviour
{
    // Hides the runtime Vuforia targets and their "(simulated)" copies in the Hierarchy
    public bool hideRuntimeClutter = true;
    // Points the AR camera at the cards when Play starts
    public bool aimCameraAtCards = true;
    public float cameraDistance = 0.45f;
    [Range(10f, 90f)] public float cameraPitch = 50f;

#if UNITY_EDITOR
    private readonly Dictionary<Transform, Transform> simulated = new Dictionary<Transform, Transform>();
    private bool isSimulator;

    void Awake()
    {
        isSimulator = VuforiaConfiguration.Instance.PlayMode.PlayModeType.ToString() == "SIMULATOR";
        if (isSimulator && aimCameraAtCards)
        {
            AimCamera();
        }
    }

    void Start()
    {
        // The simulator draws the cards itself
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            r.enabled = false;
        }
    }

    void LateUpdate()
    {
        if (!isSimulator)
        {
            return;
        }
        if (simulated.Count < transform.childCount)
        {
            FindSimulatedTargets();
        }

        foreach (var pair in simulated)
        {
            if (pair.Value != null)
            {
                pair.Value.SetPositionAndRotation(pair.Key.position, pair.Key.rotation);
            }
        }
    }

    private void FindSimulatedTargets()
    {
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (Transform card in transform)
        {
            if (simulated.ContainsKey(card))
            {
                continue;
            }
            foreach (var root in roots)
            {
                if (root.name == card.name + " (simulated)")
                {
                    simulated[card] = root.transform;
                    Hide(root);
                }
                else if (root.name == card.name && root.GetComponent<ImageTargetBehaviour>() != null)
                {
                    Hide(root);
                }
            }
        }
    }

    private void Hide(GameObject target)
    {
        if (hideRuntimeClutter)
        {
            target.hideFlags |= HideFlags.HideInHierarchy;
            UnityEditor.EditorApplication.RepaintHierarchyWindow();
        }
    }

    // Moves the camera's parent so the camera looks down at the middle of the cards
    private void AimCamera()
    {
        var setup = FindFirstObjectByType<VuforiaArenaSetup>();
        Camera cam = setup != null && setup.arCamera != null ? setup.arCamera : Camera.main;
        if (cam == null || cam.transform.parent == null)
        {
            return;
        }

        Vector3 center = Vector3.zero;
        foreach (Transform card in transform)
        {
            center += card.position;
        }
        center /= Mathf.Max(1, transform.childCount);

        Vector3 up = transform.up;
        Vector3 forward = Vector3.ProjectOnPlane(center - cam.transform.position, up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = transform.forward;
        }
        forward.Normalize();
        Vector3 view = Quaternion.AngleAxis(cameraPitch, Vector3.Cross(up, forward)) * forward;
        Quaternion cameraRotation = Quaternion.LookRotation(view, up);
        Vector3 cameraPosition = center - view * cameraDistance;

        Transform cameraTransform = cam.transform;
        Transform offset = cameraTransform.parent;
        offset.rotation = cameraRotation * Quaternion.Inverse(cameraTransform.localRotation);
        offset.position = cameraPosition - offset.rotation * Vector3.Scale(offset.lossyScale, cameraTransform.localPosition);
    }
#endif
}
