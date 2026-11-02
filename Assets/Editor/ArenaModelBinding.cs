using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Resolve the imported FBX through AssetDatabase, rather than guessing its generated root file ID.
[InitializeOnLoad]
public class ArenaModelBinding : IProcessSceneWithReport
{
    public int callbackOrder => 0;
    private const string ModelPath = "Assets/Arena Model/3d-model.fbx";

    static ArenaModelBinding()
    {
        EditorApplication.delayCall += BindLoadedScenes;
        EditorSceneManager.sceneOpened += (scene, mode) => Bind(scene);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) BindLoadedScenes();
        };
    }

    static void BindLoadedScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++) Bind(SceneManager.GetSceneAt(i));
    }

    static void Bind(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) return;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var manager in root.GetComponentsInChildren<CardWeaponManager>(true))
        {
            if (manager.arenaPrefab != null) continue;
            manager.arenaPrefab = model;
            if (!BuildPipeline.isBuildingPlayer && !EditorApplication.isPlaying)
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    public void OnProcessScene(Scene scene, BuildReport report) => Bind(scene);
}
