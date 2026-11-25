#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Chess.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click setup for the main menu: Chess ▸ Create Main Menu Scene.
/// Creates Assets/Scenes/MainMenu.unity (a camera + a MainMenu component; the UI itself is built at runtime)
/// and puts MainMenu first and SampleScene second in the Build Profiles scene list.
/// Safe to run again: it reuses the scene if it exists and never duplicates a build entry.
/// </summary>
public static class MainMenuSceneSetup
{
    const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    const string GameScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Chess/Create Main Menu Scene")]
    static void Create()
    {
        if (!File.Exists(GameScenePath))
        {
            EditorUtility.DisplayDialog("Create Main Menu Scene",
                $"Couldn't find the game scene at {GameScenePath}.\nIf you renamed it, change GameScenePath in MainMenuSceneSetup.cs " +
                "and the \"Game Scene Name\" field on the MainMenu component.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (!File.Exists(MenuScenePath))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
            cam.orthographic = true;
            camGo.transform.position = new Vector3(0, 0, -10);

            new GameObject("MainMenu").AddComponent<MainMenu>();

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }
        else
        {
            EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
        }

        // Menu first (index 0 = what a built game opens on), then the game scene; keep any other scenes the project has.
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MenuScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true),
        };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != MenuScenePath && s.path != GameScenePath) scenes.Add(s);
        EditorBuildSettings.scenes = scenes.ToArray();

        Debug.Log("[Chess] MainMenu scene ready and added to the Build Profiles scene list. Open it and press Play.");
    }
}
#endif
