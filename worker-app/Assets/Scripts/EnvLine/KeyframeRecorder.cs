using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CoGaze.Geometry;
using UnityEngine;

namespace CoGaze2.EnvLine
{
    /// <summary>
    /// Writes keyframes to disk so the reconstruction server can be developed and tuned against
    /// recorded sessions instead of a live headset.
    ///
    /// Images are stored as raw RGB rather than PNG: encoding is the expensive part, the data rate
    /// is low enough not to need it, and numpy reads raw directly.
    /// </summary>
    public class KeyframeRecorder : MonoBehaviour
    {
        [SerializeField] KeyframeSource source;
        [Tooltip("Folder under the persistent data path.")]
        [SerializeField] string sessionName = "session";

        readonly ConcurrentQueue<Keyframe> _pending = new ConcurrentQueue<Keyframe>();
        Thread _writer;
        volatile bool _running;
        string _directory;

        void OnEnable()
        {
            if (source == null) source = FindAnyObjectByType<KeyframeSource>();
            if (source == null)
            {
                Debug.LogError($"[EnvLine] no {nameof(KeyframeSource)} to record from. Disabling.");
                enabled = false;
                return;
            }

            _directory = Path.Combine(Application.persistentDataPath, sessionName);
            Directory.CreateDirectory(_directory);
            Debug.Log($"[EnvLine] recording to {_directory}");

            _running = true;
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "KeyframeRecorder" };
            _writer.Start();

            source.KeyframeReady += Enqueue;
        }

        void OnDisable()
        {
            if (source != null) source.KeyframeReady -= Enqueue;
            _running = false;
            _writer?.Join(2000);
        }

        void Enqueue(Keyframe keyframe) => _pending.Enqueue(keyframe);

        void WriteLoop()
        {
            while (_running || !_pending.IsEmpty)
            {
                if (!_pending.TryDequeue(out Keyframe keyframe))
                {
                    Thread.Sleep(10);
                    continue;
                }

                string stem = Path.Combine(_directory, keyframe.index.ToString("D5", CultureInfo.InvariantCulture));
                File.WriteAllBytes(stem + "_L.raw", keyframe.leftRgb);
                File.WriteAllBytes(stem + "_R.raw", keyframe.rightRgb);
                // UTF8Encoding(false) rather than Encoding.UTF8: the latter emits a byte order
                // mark, which json.load rejects unless the reader asks for utf-8-sig.
                File.WriteAllText(stem + ".json", Describe(keyframe), new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// Written by hand rather than via JsonUtility, which cannot express nested poses and
        /// intrinsics in a shape convenient to read from Python. Shared with
        /// <see cref="KeyframeUploader"/>, so the log a live session writes is byte-identical to a
        /// recording and reprocesses the same way.
        /// </summary>
        public static string Describe(Keyframe k)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"index\": {0},\n", k.index);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"epoch\": {0},\n", k.epoch);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"width\": {0},\n", k.width);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"height\": {0},\n", k.height);
            sb.Append("  \"convention\": \"right-handed, Y-down, world_T_camera; images top row first\",\n");
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "  \"timestamp_ticks\": {{ \"left\": {0}, \"right\": {1} }},\n",
                k.leftTimestampTicks, k.rightTimestampTicks);
            sb.Append("  \"left\": ");
            AppendCamera(sb, k.leftPoseCv, k.leftK);
            sb.Append(",\n  \"right\": ");
            AppendCamera(sb, k.rightPoseCv, k.rightK);
            sb.Append("\n}\n");
            return sb.ToString();
        }

        static void AppendCamera(StringBuilder sb, Pose pose, CameraGeometry.Intrinsics k)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "{{ \"fx\": {0}, \"fy\": {1}, \"cx\": {2}, \"cy\": {3}, " +
                "\"position\": [{4}, {5}, {6}], \"quaternion_xyzw\": [{7}, {8}, {9}, {10}] }}",
                k.fx, k.fy, k.cx, k.cy,
                pose.position.x, pose.position.y, pose.position.z,
                pose.rotation.x, pose.rotation.y, pose.rotation.z, pose.rotation.w);
        }
    }
}
