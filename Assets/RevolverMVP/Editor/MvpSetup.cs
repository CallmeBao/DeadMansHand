using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RevolverMVP
{
    /// <summary>Creates the self-contained MVP scene and builds the standalone player.</summary>
    public static class MvpSetup
    {
        public const string ScenePath = "Assets/RevolverMVP/Scenes/SingleMatch.unity";

        [MenuItem("Revolver MVP/Open Match")]
        public static void OpenMatch()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureScene();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Revolver MVP/Run Checks")]
        public static void Verify()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play mode before running checks.");
            RuleChecks.Run();
            PhysicsChecks.Run();
            AuthorityChecks.Run();
            Debug.Log("REVOLVER MVP: rule and physics checks passed.");
        }

        [MenuItem("Revolver MVP/Build Windows Player")]
        public static void PrepareAndBuild()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play mode before building.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureScene();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            Verify();
            Directory.CreateDirectory("Build");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Build/RevolverMVP.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Revolver MVP build failed: " + report.summary.result);
            File.WriteAllText("verification.txt",
                "PASS: rule and physics checks; Windows x64 player build.\nUnity " + Application.unityVersion + "\n");
            Debug.Log("REVOLVER MVP: Windows build succeeded at Build/RevolverMVP.exe");
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Revolver MVP";
            PlayerSettings.companyName = "Revolver Prototype";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void EnsureScene()
        {
            // Never replace a scene the user has already edited.
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory("Assets/RevolverMVP/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.055f);
            new GameObject("Match Controller").AddComponent<MatchController>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }
    }
}
