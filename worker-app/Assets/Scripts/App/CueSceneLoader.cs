using System.Collections.Generic;
using CoGaze.Core;
using CoGaze.Worker.Cue;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoGaze.Worker.App
{
    /// <summary>
    /// Keeps the cue scene the condition names (CueScenes) loaded additively and hands its renderers to
    /// the CuePipeline through the shared list. Swaps only between trials: a condition that changes
    /// mid-trial is logged and the swap waits for the trial to end. Every load, unload and failure is
    /// an events.csv row.
    /// </summary>
    sealed class CueSceneLoader
    {
        readonly IConditionSource _condition;
        readonly List<ICueRenderer> _renderers;
        readonly ILogSink _log;
        readonly IClock _clock;
        string _loaded, _loading, _failed, _deferred;
        AsyncOperation _op;

        public CueSceneLoader(IConditionSource condition, List<ICueRenderer> renderers, ILogSink log, IClock clock)
        {
            _condition = condition;
            _renderers = renderers;
            _log = log;
            _clock = clock;
        }

        public void Tick()
        {
            if (_op != null)
            {
                if (!_op.isDone) return;
                Finish();
            }
            string wanted = CueScenes.Wanted(_condition);
            if (wanted == null || wanted == _loaded || wanted == _failed) return;
            if (_condition.InTrial)
            {
                if (_deferred != wanted) _log.Event(_clock.NowNs, "cue_scene_deferred", $"{wanted} wanted during trial {_condition.TrialIndex}; showing {_loaded ?? "nothing"}");
                _deferred = wanted;
                return;
            }
            Begin(wanted);
        }

        void Begin(string scene)
        {
            _deferred = null;
            if (_loaded != null)
            {
                _renderers.Clear();
                SceneManager.UnloadSceneAsync(_loaded);
                _log.Event(_clock.NowNs, "cue_scene_unload", _loaded);
                _loaded = null;
            }
            _op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive);
            if (_op == null)
            {
                _failed = scene;
                _log.Event(_clock.NowNs, "cue_scene_missing", $"{scene} is not in the build settings");
                Debug.LogError($"[Worker] cue scene {scene} is not in the build settings");
                return;
            }
            _loading = scene;
            _log.Event(_clock.NowNs, "cue_scene_load", scene);
        }

        void Finish()
        {
            _op = null;
            _loaded = _loading;
            _loading = null;
            foreach (var root in SceneManager.GetSceneByName(_loaded).GetRootGameObjects())
                _renderers.AddRange(root.GetComponentsInChildren<ICueRenderer>());
            _log.Event(_clock.NowNs, "cue_scene_ready", $"{_loaded} renderers={_renderers.Count}");
            if (_renderers.Count == 0) Debug.LogError($"[Worker] cue scene {_loaded} holds no ICueRenderer");
        }
    }
}
