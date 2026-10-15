using System;
using CoGaze.Core;
using Eyeware.BeamEyeTracker;

namespace CoGaze.Expert.Sources.Beam
{
    /// <summary>
    /// Beam Eye Tracker SDK as an IGazeSource ("beam"). Polls the latest tracking state each frame;
    /// a new SDK timestamp means a new sample. Compiled only with COGAZE_BEAM (the SDK cannot be
    /// committed — see Assets/ThirdParty/Beam/README.md).
    ///
    /// Time: the SDK does not document which clock its timestamp uses, so samples are stamped with
    /// our clock on read (COM-F-002); the SDK time is kept only to detect new samples.
    /// Pixels: Beam's unified screen space is OS logical pixels; px_scale converts to physical px.
    /// </summary>
    public sealed class BeamGazeSource : IGazeSource, IDisposable
    {
        public const string SourceName = "beam";

        readonly API _api;
        readonly IClock _clock;
        readonly float _pxScale;
        double _lastTimestamp = Constants.NullDataTimestamp;

        public BeamGazeSource(IClock clock, int screenWidthPx, int screenHeightPx, float pxScale)
        {
            _clock = clock;
            _pxScale = pxScale;
            _api = new API("CoGaze", new ViewportGeometry(new Point(0, 0), new Point(screenWidthPx - 1, screenHeightPx - 1)));
            _api.AttemptStartingTheBeamEyeTracker();
        }

        public string Name => SourceName;

        public bool IsReceiving => _api.GetTrackingDataReceptionStatus() == TrackingDataReceptionStatus.ReceivingTrackingData;

        public bool TryRead(out GazeSample sample)
        {
            var user = _api.GetLatestTrackingStateSet().UserState;
            double ts = user.TimestampInSeconds;
            if (ts == Constants.NullDataTimestamp || ts == _lastTimestamp)
            {
                sample = default;
                return false;
            }
            _lastTimestamp = ts;
            long now = _clock.NowNs;
            var gaze = user.UnifiedScreenGaze;
            int confidence = Level(gaze.Confidence);
            // Unbounded, so a look off the screen stays off it and View2D marks it invalid.
            var p = gaze.UnboundedPointOfRegard;
            sample = new GazeSample(SourceName, now, now, confidence > 0, p.X * _pxScale, p.Y * _pxScale,
                                    GazeMode.Eye, float.NaN, confidence);
            return true;
        }

        /// <summary>LostTracking 0, Low 1, Medium 2, High 3 — matches the /cogaze/beam confidence (SRS 3.2).</summary>
        static int Level(TrackingConfidence c)
        {
            switch (c)
            {
                case TrackingConfidence.High: return 3;
                case TrackingConfidence.Medium: return 2;
                case TrackingConfidence.Low: return 1;
                default: return 0;
            }
        }

        public void Dispose() => _api.Dispose();
    }
}
