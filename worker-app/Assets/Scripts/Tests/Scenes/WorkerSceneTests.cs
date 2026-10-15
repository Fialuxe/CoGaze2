using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CoGaze.Worker.SceneTests
{
    /// <summary>
    /// Plays Worker.unity in editor rehearsal, which cycles Circle → Ray → Frustum every 6 s, and checks
    /// that the cue scenes are swapped additively. Engine-dependent, so outside the dotnet test build.
    /// </summary>
    public class WorkerSceneTests
    {
        static bool Loaded(string name) => SceneManager.GetSceneByName(name).isLoaded;

        static IEnumerator WaitFor(Func<bool> condition, float timeoutS)
        {
            float end = Time.realtimeSinceStartup + timeoutS;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTest]
        public IEnumerator Rehearsal_SwapsCueScenesAsTheVisualisationCycles()
        {
            LogAssert.ignoreFailingMessages = true;   // no headset in the test run; the XR stack logs errors
            EditorSceneManager.OpenScene("Assets/Scenes/Worker.unity");
            yield return new EnterPlayMode();

            yield return WaitFor(() => Loaded("Cue_Circle"), 4f);
            Assert.IsTrue(Loaded("Cue_Circle"), "Cue_Circle not loaded for the first rehearsal condition");
            Assert.IsFalse(Loaded("Cue_Ray"));

            yield return WaitFor(() => Loaded("Cue_Ray") && !Loaded("Cue_Circle"), 9f);
            Assert.IsTrue(Loaded("Cue_Ray"), "Cue_Ray not loaded after the cycle moved on");
            Assert.IsFalse(Loaded("Cue_Circle"), "Cue_Circle still loaded after the swap");
            Assert.AreEqual(2, SceneManager.sceneCount, "expected the base scene plus exactly one cue scene");

            yield return new ExitPlayMode();
        }
    }
}
