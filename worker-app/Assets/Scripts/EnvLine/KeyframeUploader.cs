using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace CoGaze2.EnvLine
{
    /// <summary>
    /// Worker side: streams keyframes to the reconstruction service over a WebSocket. The
    /// replacement for <see cref="KeyframeRecorder"/>'s disk write once a live server exists —
    /// same source, same metadata, JPEG instead of raw so it fits the headset's WiFi.
    ///
    /// The message layout mirrors <c>service.py</c>'s <c>decode_keyframe</c>: a "CGZK" header, the
    /// recording-format JSON (so the server's log is a recording), then the two JPEGs. Encoding
    /// happens on the main thread — the SDK image data is only valid there and a q95 encode is a
    /// few milliseconds — and a background loop drains the queue onto the socket.
    /// </summary>
    public class KeyframeUploader : MonoBehaviour
    {
        [SerializeField] KeyframeSource source;
        [Tooltip("Reconstruction service, e.g. ws://192.168.1.10:8765/worker")]
        [SerializeField] string url = "ws://127.0.0.1:8765/worker";
        [Range(1, 100)]
        [SerializeField] int jpegQuality = 95;

        readonly ConcurrentQueue<byte[]> _outbox = new ConcurrentQueue<byte[]>();
        ClientWebSocket _socket;
        CancellationTokenSource _cancel;

        async void OnEnable()
        {
            if (source == null) source = FindAnyObjectByType<KeyframeSource>();
            if (source == null)
            {
                Debug.LogError($"[EnvLine] no {nameof(KeyframeSource)} to upload from. Disabling.");
                enabled = false;
                return;
            }

            _cancel = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            try
            {
                await _socket.ConnectAsync(new Uri(url), _cancel.Token);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EnvLine] could not connect to {url}: {e.Message}. Disabling.");
                enabled = false;
                return;
            }

            Debug.Log($"[EnvLine] uploading keyframes to {url}");
            source.KeyframeReady += Enqueue;
            _ = SendLoop();
        }

        void OnDisable()
        {
            if (source != null) source.KeyframeReady -= Enqueue;
            _cancel?.Cancel();
            _socket?.Dispose();
        }

        void Enqueue(Keyframe keyframe) => _outbox.Enqueue(Encode(keyframe));

        async Task SendLoop()
        {
            while (!_cancel.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                if (_outbox.TryDequeue(out byte[] message))
                    await _socket.SendAsync(new ArraySegment<byte>(message),
                                            WebSocketMessageType.Binary, true, _cancel.Token);
                else
                    await Task.Delay(5, _cancel.Token);
            }
        }

        /// <summary>
        /// Packs one keyframe into the wire message. Rows are already top-first (OpenCV order) in
        /// <see cref="KeyframeSource"/>; the JPEG carries them as given.
        /// </summary>
        byte[] Encode(Keyframe keyframe)
        {
            byte[] json = Encoding.UTF8.GetBytes(KeyframeRecorder.Describe(keyframe));
            byte[] left = ImageConversion.EncodeArrayToJPG(
                keyframe.leftRgb, GraphicsFormat.R8G8B8_UNorm,
                (uint)keyframe.width, (uint)keyframe.height, 0, jpegQuality);
            byte[] right = ImageConversion.EncodeArrayToJPG(
                keyframe.rightRgb, GraphicsFormat.R8G8B8_UNorm,
                (uint)keyframe.width, (uint)keyframe.height, 0, jpegQuality);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { (byte)'C', (byte)'G', (byte)'Z', (byte)'K' });
            writer.Write((ushort)1);                 // version
            writer.Write((uint)json.Length);
            writer.Write(json);
            writer.Write((uint)left.Length);
            writer.Write(left);
            writer.Write((uint)right.Length);
            writer.Write(right);
            return stream.ToArray();
        }
    }
}
