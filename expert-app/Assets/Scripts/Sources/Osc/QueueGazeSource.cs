using System.Collections.Concurrent;
using CoGaze.Core;

namespace CoGaze.Expert.Sources.Osc
{
    /// <summary>An IGazeSource that drains one of the router's screen-pixel queues (tobii, beam over OSC).</summary>
    public sealed class QueueGazeSource : IGazeSource
    {
        readonly ConcurrentQueue<GazeSample> _queue;

        public QueueGazeSource(string name, ConcurrentQueue<GazeSample> queue)
        {
            Name = name;
            _queue = queue;
        }

        public string Name { get; }

        public bool TryRead(out GazeSample sample) => _queue.TryDequeue(out sample);
    }
}
