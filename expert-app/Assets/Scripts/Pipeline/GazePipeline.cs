using System;
using System.Collections.Generic;
using CoGaze.Core;

namespace CoGaze.Expert.Pipeline
{
    /// <summary>
    /// The single path from gaze sources to the worker (EXP-F-030). One Tick per frame, in this order:
    /// <list type="number">
    /// <item>read every source; every sample is logged to display.csv (EXP-F-032)</item>
    /// <item>keep only the condition's display_source; "none" stops here (EXP-F-030, 031)</item>
    /// <item>filter, logging before and after (EXP-F-040)</item>
    /// <item>project to a ray — 2D or 3D is only which projector was injected</item>
    /// <item>decide sigma, always, even when the condition does not show it (EXP-F-047, 049)</item>
    /// <item>send at most cue_rate_hz, logging the source that was sent (EXP-F-033, 043)</item>
    /// </list>
    /// No other code holds the ICueSender, so no other path to the worker exists.
    /// </summary>
    public sealed class GazePipeline
    {
        readonly IReadOnlyList<IGazeSource> _sources;
        readonly IConditionSource _conditions;
        readonly IGazeRayProjector _projector;
        readonly ISigmaPolicy _sigma;
        readonly ICueSender _sender;
        readonly ILogSink _log;
        readonly IClock _clock;
        readonly IReadOnlyDictionary<string, FilterSettings> _filters;
        readonly long _minIntervalNs;

        string _activeConditionId;
        GazeFilter _filter;
        long _lastSentNs;
        bool _sentAny;
        uint _seq;

        public GazePipeline(IReadOnlyList<IGazeSource> sources, IConditionSource conditions, IGazeRayProjector projector,
                            ISigmaPolicy sigma, ICueSender sender, ILogSink log, IClock clock,
                            IReadOnlyDictionary<string, FilterSettings> filters, float cueRateHz)
        {
            _sources = sources;
            _conditions = conditions;
            _projector = projector;
            _sigma = sigma;
            _sender = sender;
            _log = log;
            _clock = clock;
            _filters = filters;
            _minIntervalNs = (long)(1e9 / cueRateHz);
        }

        /// <summary>Cues sent since start, for the checklist and tests.</summary>
        public int CuesSent { get; private set; }

        /// <summary>Samples received per source name since start.</summary>
        public Dictionary<string, int> Received { get; } = new Dictionary<string, int>();

        public void Tick()
        {
            bool hasCondition = _conditions.HasCondition;
            var condition = hasCondition ? _conditions.Current : default;
            if (hasCondition && condition.ConditionId != _activeConditionId)
            {
                _activeConditionId = condition.ConditionId;
                _filter = new GazeFilter(FilterFor(condition.Filter));
            }

            foreach (var source in _sources)
            {
                while (source.TryRead(out var sample))
                {
                    Received.TryGetValue(sample.Source, out int n);
                    Received[sample.Source] = n + 1;

                    if (!hasCondition || !condition.SendsGaze || sample.Source != condition.DisplaySource)
                    {
                        LogRow(condition, hasCondition, sample, sample.WithScreen(float.NaN, float.NaN), GazeRay.Invalid, float.NaN, false, 0);
                        continue;
                    }
                    Process(condition, sample);
                }
            }
        }

        void Process(in Condition condition, in GazeSample raw)
        {
            var filtered = _filter.Apply(raw);
            if (!_projector.TryProject(filtered, out var ray)) ray = GazeRay.Invalid;
            float sigma = _sigma.SigmaDeg(filtered, ray);

            long now = _clock.NowNs;
            bool send = !_sentAny || now - _lastSentNs >= _minIntervalNs;
            uint seq = 0;
            if (send)
            {
                seq = ++_seq;
                var cue = new GazeCue(seq, raw.Source, raw.Valid && ray.Valid, raw.Mode, ray.U, ray.V,
                                      ray.VideoFrameId, sigma, now);
                if (cue.Source != condition.DisplaySource)   // unreachable by construction; checked because EXP-F-033 says so
                {
                    _log.Event(now, EventKinds.SourceMismatch, $"{condition.DisplaySource}!={cue.Source}");
                    return;
                }
                _sender.Send(cue);
                _lastSentNs = now;
                _sentAny = true;
                CuesSent++;
            }
            LogRow(condition, true, raw, filtered, ray, sigma, send, seq);
        }

        FilterSettings FilterFor(string name) =>
            name != null && _filters.TryGetValue(name, out var f) ? f
            : throw new InvalidOperationException($"Filter profile '{name}' missing; ConditionSet validation should have caught this.");

        void LogRow(in Condition c, bool hasCondition, in GazeSample raw, in GazeSample filt, in GazeRay ray,
                    float sigma, bool sent, uint seq)
        {
            _log.Write(LogSchema.Display, Csv.Row(
                _clock.NowNs,
                hasCondition ? c.ConditionId : "",
                _conditions.TrialIndex,
                hasCondition ? c.DisplaySource : "",
                raw.Source,
                raw.TCaptureNs,
                raw.Valid,
                raw.Mode.ToString().ToLowerInvariant(),
                raw.XPx, raw.YPx,
                filt.XPx, filt.YPx,
                ray.U, ray.V,
                ray.VideoFrameId,
                hasCondition && c.ShowUncertainty,
                hasCondition ? SigmaSourceNames.Of(c.SigmaSource) : "",
                sigma,
                sent,
                sent ? raw.Source : "",
                sent ? (object)seq : null));
        }
    }
}
