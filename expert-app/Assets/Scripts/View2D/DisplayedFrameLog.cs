namespace CoGaze.Expert.View2D
{
    /// <summary>
    /// Which worker video frame was on screen when (EXP-F-023). The video panel records each frame as it
    /// is presented; the projector asks which one was showing at a gaze sample's capture time.
    /// </summary>
    public sealed class DisplayedFrameLog
    {
        readonly long[] _t, _id;
        int _count, _head;

        public DisplayedFrameLog(int capacity = 512)
        {
            _t = new long[capacity];
            _id = new long[capacity];
        }

        public void Record(long tShownNs, long videoFrameId)
        {
            _t[_head] = tShownNs;
            _id[_head] = videoFrameId;
            _head = (_head + 1) % _t.Length;
            if (_count < _t.Length) _count++;
        }

        /// <summary>The frame shown at tNs; -1 when nothing had been shown yet.</summary>
        public long FrameAt(long tNs)
        {
            for (int k = 1; k <= _count; k++)
            {
                int i = (_head - k + _t.Length) % _t.Length;
                if (_t[i] <= tNs) return _id[i];
            }
            return -1;
        }
    }
}
