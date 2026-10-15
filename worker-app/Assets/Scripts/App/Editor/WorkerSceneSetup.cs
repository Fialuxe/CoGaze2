using System;
using System.IO;
using System.Linq;
using CoGaze.Core;
using CoGaze.Worker.Cue;
using CoGaze.Worker.Cue.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoGaze.Worker.App.Editor
{
    /// <summary>
    /// Generates the worker's scenes rather than committing hand-written YAML:
    /// Worker.unity, the base scene (the Quest passthrough rig from Tutorial.unity, minus its keyframe
    /// capturer, plus the bootstrap), and one additive Cue_&lt;Visualization&gt;.unity per renderer.
    /// Missing scenes are created when the project opens; the build lists the base first, then the cues.
    /// </summary>
    public static class WorkerSceneSetup
    {
        const string Dir = "Assets/Scenes";
        const string BasePath = Dir + "/Worker.unity";
        const string RigSource = Dir + "/Tutorial.unity";
        const string EnvLineCapturer = "FrameCapturer";

        static string CuePath(Visualization v) => $"{Dir}/{CueScenes.For(v)}.unity";
        static Visualization[] Kinds => (Visualization[])Enum.GetValues(typeof(Visualization));

        [InitializeOnLoadMethod]
        static void EnsureOnLoad() => EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Ensure();
        };

        /// <summary>Creates whichever scenes are missing. Also the batch entry: -executeMethod CoGaze.Worker.App.Editor.WorkerSceneSetup.Ensure</summary>
        public static void Ensure()
        {
            bool needBase = !File.Exists(BasePath);
            var cues = Kinds.Where(v => !File.Exists(CuePath(v))).ToArray();
            if (needBase || cues.Length > 0) Generate(needBase, cues);
            UpdateBuildSettings();
        }

        [MenuItem("CoGaze/Recreate Worker Scenes")]
        static void RecreateAll()
        {
            Generate(true, Kinds);
            UpdateBuildSettings();
        }

        /// <summary>Opens each scene on its own, then reopens what was open (after offering to save it).</summary>
        static void Generate(bool createBase, Visualization[] cues)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup().Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
            if (createBase) CreateBase();
            foreach (var v in cues) CreateCue(v);
            if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        static void CreateBase()
        {
            AssetDatabase.DeleteAsset(BasePath);
            if (!AssetDatabase.CopyAsset(RigSource, BasePath))
            {
                Debug.LogError($"[CoGaze] Cannot create {BasePath}: {RigSource} (the passthrough rig) is missing");
                return;
            }
            var scene = EditorSceneManager.OpenScene(BasePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == EnvLineCapturer))
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            new GameObject("Bootstrap").AddComponent<WorkerBootstrapBehaviour>();
            Save(scene, BasePath);
        }

        static void CreateCue(Visualization v)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject(v + "Cue").AddComponent(RendererType(v));
            Save(scene, CuePath(v));
        }

        static Type RendererType(Visualization v) => v switch
        {
            Visualization.Ray => typeof(RayCueRendererBehaviour),
            Visualization.Circle => typeof(CircleCueRendererBehaviour),
            Visualization.Frustum => typeof(FrustumCueRendererBehaviour),
            _ => throw new ArgumentOutOfRangeException(nameof(v), v, "no renderer for this visualization"),
        };

        static void Save(Scene scene, string path)
        {
            Directory.CreateDirectory(Dir);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[CoGaze] Created {path}");
        }

        static void UpdateBuildSettings()
        {
            var ours = new[] { BasePath }.Concat(Kinds.Select(CuePath)).ToArray();
            var others = EditorBuildSettings.scenes.Where(s => !ours.Contains(s.path));
            EditorBuildSettings.scenes = ours.Select(p => new EditorBuildSettingsScene(p, true)).Concat(others).ToArray();
        }
    }
}
