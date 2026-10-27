using UnityEngine;
using Vuforia;

// Configure the engine before adding VuforiaBehaviour (which initializes it).
[DefaultExecutionOrder(-1000)]
public class VuforiaArenaSetup : MonoBehaviour
{
    public Camera arCamera;
    [Tooltip("Path relative to StreamingAssets; keep the matching .dat beside the XML.")]
    public string databasePath = "Vuforia/arena.xml";
    public string[] targetNames = { "sword", "axe", "shield", "dagger", "hammer", "sword2", "axe2", "shield2", "dagger2", "hammer2", "player1", "player2" };
    private bool targetsCreated;

    void Awake()
    {
        var config = VuforiaConfiguration.Instance;
        // Every card can be on the table at the same time
        config.Vuforia.MaxSimultaneousImageTargets = Mathf.Max(1, targetNames.Length);
        config.DeviceTracker.ARCoreRequirementSetting = VuforiaConfiguration.DeviceTrackerConfiguration.ARCoreRequirement.DONT_USE;
        config.DeviceTracker.AutoImportArcoreSetting = false;
        config.DeviceTracker.AutoInitAndStartTracker = false;
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) { Debug.LogError("Vuforia: falta a camera.", this); enabled = false; return; }
        VuforiaApplication.Instance.OnVuforiaStarted += CreateTargets;
        if (arCamera.GetComponent<VuforiaBehaviour>() == null)
            arCamera.gameObject.AddComponent<VuforiaBehaviour>();
    }

    void CreateTargets()
    {
        if (targetsCreated) return;
        targetsCreated = true;
        var manager = GetComponent<CardWeaponManager>();
        foreach (var targetName in targetNames)
        {
            if (string.IsNullOrWhiteSpace(targetName)) continue;
            try
            {
                var target = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTarget(databasePath, targetName);
                if (target == null) { Debug.LogError($"Vuforia: nao foi possivel carregar {targetName} de {databasePath}.", this); continue; }
                target.SetTrackingOptimization(TrackingOptimization.AR_CONTROLLER);
                manager.RegisterTarget(target);
            }
            catch (System.Exception exception) { Debug.LogException(exception, this); }
        }
    }

    void OnDestroy()
    {
        VuforiaApplication.Instance.OnVuforiaStarted -= CreateTargets;
    }
}
