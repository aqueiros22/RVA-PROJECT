using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Vuforia;

// Persist these settings so the Vuforia build processor also excludes ARCore,
// and keep the Android API level Vuforia needs (it runs before Vuforia's own build checks).
[InitializeOnLoad]
public class VuforiaArenaConfiguration : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    // Vuforia Engine refuses to build for anything below Android 10
    private const AndroidSdkVersions MinimumAndroidApi = AndroidSdkVersions.AndroidApiLevel29;
    // The asset that goes into the build; VuforiaConfiguration.Instance can be an in-memory copy of it
    private const string ConfigurationPath = "Assets/Resources/VuforiaConfiguration.asset";
    private const string DatabasePath = "Assets/StreamingAssets/Vuforia/arena.xml";

    static VuforiaArenaConfiguration()
    {
        EditorApplication.delayCall += Configure;
    }

    static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var asset = AssetDatabase.LoadAssetAtPath<VuforiaConfiguration>(ConfigurationPath);
        Apply(asset);
        if (VuforiaConfiguration.Instance != asset)
            Apply(VuforiaConfiguration.Instance);
        // Only raise it, so a higher level chosen later is kept
        if (PlayerSettings.Android.minSdkVersion < MinimumAndroidApi)
            PlayerSettings.Android.minSdkVersion = MinimumAndroidApi;
        AssetDatabase.SaveAssets();
    }

    static void Apply(VuforiaConfiguration config)
    {
        if (config == null) return;
        // Same cap as at runtime (fewer if the database has fewer cards)
        config.Vuforia.MaxSimultaneousImageTargets = Mathf.Min(CountDatabaseTargets(), VuforiaArenaSetup.MaxTrackedCards);
        config.DeviceTracker.ARCoreRequirementSetting = VuforiaConfiguration.DeviceTrackerConfiguration.ARCoreRequirement.DONT_USE;
        config.DeviceTracker.AutoImportArcoreSetting = false;
        config.DeviceTracker.AutoInitAndStartTracker = false;
        EditorUtility.SetDirty(config);
    }

    static int CountDatabaseTargets()
    {
        if (!File.Exists(DatabasePath)) return 1;
        int count = Regex.Matches(File.ReadAllText(DatabasePath), "<ImageTarget ").Count;
        return count > 0 ? count : 1;
    }

    [MenuItem("Arena/Configurar Vuforia")]
    static void SelectConfiguration()
    {
        Configure();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<VuforiaConfiguration>(ConfigurationPath);
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        Configure();
        var asset = AssetDatabase.LoadAssetAtPath<VuforiaConfiguration>(ConfigurationPath);
        if (asset == null || string.IsNullOrWhiteSpace(asset.Vuforia.LicenseKey))
            throw new BuildFailedException("Falta a App License Key. Abrir Arena > Configurar Vuforia e preencher a chave.");
    }
}
