using System.Collections.Concurrent;
using System.Collections.Generic;
using CoGaze.Core;

namespace CoGaze.Expert.Sources.Osc
{
    /// <summary>
    /// Turns OSC messages into typed queues. Thread-safe: the UDP thread calls Route; the main thread
    /// drains through the sources and the status table. Timestamps come from the sender (COM-F-002);
    /// receive time is stamped here.
    /// </summary>
    public sealed class OscRouter
    {
        readonly IClock _clock;
        readonly float _beamPxScale;

        public OscRouter(IClock clock, float beamPxScale = 1f)
        {
            _clock = clock;
            _beamPxScale = beamPxScale;
        }

        public ConcurrentQueue<GazeSample> Tobii { get; } = new ConcurrentQueue<GazeSample>();
        public ConcurrentQueue<GazeSample> Beam { get; } = new ConcurrentQueue<GazeSample>();
        public ConcurrentQueue<EstimatorSample> Estimator { get; } = new ConcurrentQueue<EstimatorSample>();
        public ProcessStatusTable Status { get; } = new ProcessStatusTable();

        /// <summary>Messages with a known address but the wrong argument count. Shown on the checklist.</summary>
        public int Malformed { get; private set; }

        public void Route(IEnumerable<OscMessage> messages)
        {
            long now = _clock.NowNs;
            foreach (var m in messages)
            {
                switch (m.Address)
                {
                    case OscAddresses.Tobii when m.Args.Count >= 4:
                        Tobii.Enqueue(new GazeSample("tobii", m.Long(0), now, m.Int(3) != 0, m.Float(1), m.Float(2)));
                        break;
                    case OscAddresses.Beam when m.Args.Count >= 10:
                        int conf = m.Int(9);
                        Beam.Enqueue(new GazeSample("beam", m.Long(0), now, conf > 0,
                            m.Float(1) * _beamPxScale, m.Float(2) * _beamPxScale, GazeMode.Eye, float.NaN, conf));
                        break;
                    case OscAddresses.Gaze when m.Args.Count >= 19:
                        Estimator.Enqueue(EstimatorSample.From(m, now));
                        break;
                    case OscAddresses.Status when m.Args.Count >= 4:
                        Status.Update(m.Str(0), m.Long(1), m.Str(2), m.Float(3), now);
                        break;
                    case OscAddresses.Tobii:
                    case OscAddresses.Beam:
                    case OscAddresses.Gaze:
                    case OscAddresses.Status:
                        Malformed++;
                        break;
                }
            }
        }
    }
}
