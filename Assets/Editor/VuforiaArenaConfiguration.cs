using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Vuforia;

// Persist these settings so the Vuforia build processor also excludes ARCore.
[InitializeOnLoad]
public class VuforiaArenaConfiguration : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    static VuforiaArenaConfiguration()
    {
        EditorApplication.delayCall += Configure;
    }

    static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var config = VuforiaConfiguration.Instance;
        config.Vuforia.MaxSimultaneousImageTargets = 4;
        config.DeviceTracker.ARCoreRequirementSetting = VuforiaConfiguration.DeviceTrackerConfiguration.ARCoreRequirement.DONT_USE;
        config.DeviceTracker.AutoImportArcoreSetting = false;
        config.DeviceTracker.AutoInitAndStartTracker = false;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Arena/Configurar Vuforia")]
    static void SelectConfiguration()
    {
        Configure();
        Selection.activeObject = VuforiaConfiguration.Instance;
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        Configure();
        if (string.IsNullOrWhiteSpace(VuforiaConfiguration.Instance.Vuforia.LicenseKey))
            throw new BuildFailedException("Falta a App License Key. Abrir Arena > Configurar Vuforia e preencher a chave.");
    }
}
