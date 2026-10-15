using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoGaze.Expert.App.Editor
{
    /// <summary>
    /// Creates Assets/Scenes/Expert.unity (one camera, one bootstrap) the first time the project is
    /// opened, and puts it first in the build. Generated rather than committed as hand-written YAML.
    /// </summary>
    static class ExpertSceneSetup
    {
        const string ScenePath = "Assets/Scenes/Expert.unity";

        [InitializeOnLoadMethod]
        static void EnsureOnLoad() => EditorApplication.delayCall += () =>
        {
            if (!System.IO.File.Exists(ScenePath)) Create();
        };

        [MenuItem("CoGaze/Recreate Expert Scene")]
        static void Create()
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(0x0B, 0x0E, 0x13, 0xFF);

            new GameObject("Bootstrap").AddComponent<ExpertBootstrapBehaviour>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var others = EditorBuildSettings.scenes.Where(s => s.path != ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(others).ToArray();
            Debug.Log($"[CoGaze] Created {ScenePath}");
        }
    }
}
