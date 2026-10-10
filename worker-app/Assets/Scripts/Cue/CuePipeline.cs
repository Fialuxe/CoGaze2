using System;
using System.Collections.Generic;
using System.Numerics;
using CoGaze.Core;

namespace CoGaze.Worker.Cue
{
    /// <summary>
    /// The worker's half of the gaze path, one Tick per displayed frame:
    /// receive → pose of the frame the expert saw (or the latest, logged) → uv to ray → work plane →
    /// pick the renderer the condition names → size from sigma or the default → log cue.csv.
    /// Sigma is used as sent; the worker never recomputes it (EXP-F-047, WRK-F-023).
    /// </summary>
    public sealed class CuePipeline
    {
        readonly ICueReceiver _receiver;
        readonly IConditionSource _condition;
        readonly IFramePoseHistory _poses;
        readonly IWorkSurface _surface;
        readonly IReadOnlyList<ICueRenderer> _renderers;
        readonly ILogSink _log;
        readonly IClock _clock;
        readonly CueStyle _style;

        GazeCue _cue;
        bool _haveCue;
        long _cueArrivedNs;
        CueVisual _lastVisual;
        bool _haveVisual;
        float _opacity;

        public CuePipeline(ICueReceiver receiver, IConditionSource condition, IFramePoseHistory poses, IWorkSurface surface,
                           IReadOnlyList<ICueRenderer> renderers, ILogSink log, IClock clock, CueStyle style)
        {
            _receiver = receiver;
            _condition = condition;
            _poses = poses;
            _surface = surface;
            _renderers = renderers;
            _log = log;
            _clock = clock;
            _style = style;
        }

        public void Tick(float deltaS)
        {
            long now = _clock.NowNs;
            while (_receiver.TryReceive(out var c))
            {
                _cue = c;
                _haveCue = true;
                _cueArrivedNs = now;
            }

            if (!_condition.HasCondition || !_condition.Current.SendsGaze)   // WRK-F-027
            {
                HideAll();
                _haveVisual = false;
                _opacity = 0f;
                return;
            }

            var condition = _condition.Current;
            bool fresh = _haveCue && (now - _cueArrivedNs) / 1e9 <= _style.StaleS;
            bool poseFallback = false;
            bool placed = fresh && _cue.Valid && Place(condition, out _lastVisual, out poseFallback);
            if (placed) _haveVisual = true;

            // Fade in/out over FadeS instead of popping (WRK-F-026).
            float step = _style.FadeS > 0 ? deltaS / _style.FadeS : 1f;
            _opacity = placed ? 1f : Math.Max(0f, _opacity - step);

            var active = Renderer(condition.Visualization);
            foreach (var r in _renderers)
                if (r != active) r.Hide();
            if (_haveVisual && _opacity > 0f)
            {
                var v = _lastVisual;
                active?.Show(new CueVisual(v.Origin, v.Hit, v.Normal, v.RadiusM, v.HalfAngleDeg, _opacity));
            }
            else active?.Hide();

            if (_haveCue) LogRow(now, condition, placed, poseFallback);
        }

        bool Place(in Condition condition, out CueVisual visual, out bool poseFallback)
        {
            visual = default;
            poseFallback = false;
            if (!_poses.TryGet(_cue.VideoFrameId, out var frame))
            {
                if (!_poses.TryLatest(out frame)) return false;
                poseFallback = true;   // WRK-F-021
            }
            if (!_surface.TryGetPlane(out var plane)) return false;
            CueMath.Unproject(frame, _cue.U, _cue.V, out var origin, out var dir);
            if (!CueMath.Intersect(origin, dir, plane, out var hit, out float distance)) return false;

            float sigma = condition.ShowUncertainty ? _cue.SigmaDeg : _style.DefaultSigmaDeg;
            visual = new CueVisual(origin, hit, plane.Normal, CueMath.RadiusForSigma(distance, sigma), sigma, 1f);
            return true;
        }

        ICueRenderer Renderer(Visualization kind)
        {
            foreach (var r in _renderers)
                if (r.Kind == kind) return r;
            return null;
        }

        void HideAll()
        {
            foreach (var r in _renderers) r.Hide();
        }

        void LogRow(long now, in Condition c, bool placed, bool poseFallback)
        {
            var v = _lastVisual;
            _log.Write(LogSchema.Cue, Csv.Row(now, _cue.Seq, _cue.Source, _cue.Valid, poseFallback, placed,
                placed ? (object)v.Hit.X : null, placed ? (object)v.Hit.Y : null, placed ? (object)v.Hit.Z : null,
                placed ? (object)v.RadiusM : null, placed ? (object)v.HalfAngleDeg : null,
                c.Visualization.ToString().ToLowerInvariant(), c.ShowUncertainty, _cue.SigmaDeg));
        }
    }
}
