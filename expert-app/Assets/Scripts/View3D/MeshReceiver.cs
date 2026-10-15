using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using CoGaze.Core;
using CoGaze.Expert.View3D.Model;
using CoGaze.Geometry;
using Draco;
using Unity.Collections;
using UnityEngine;

namespace CoGaze.Expert.View3D
{
    /// <summary>
    /// Rebuilds the worker's space from chunk updates and keeps it current as chunks change.
    ///
    /// Two sources feed the same <see cref="ApplyUpdate"/>. Live, it receives from
    /// <c>server/service.py</c> /expert over a WebSocket and reconnects when the service is not up;
    /// replay reads the .cgzm files written by <c>server/run_chunks.py</c> or
    /// <c>server/make_synthetic_chunks.py</c> — the staging path that isolates whether Unity renders
    /// our geometry in the right place from whether the transport works. The bytes are identical
    /// either way, so switching source changes nothing downstream. Progress and failures go to
    /// <see cref="ReconstructionStatus"/>, which the checklist and the stage label show.
    /// </summary>
    sealed class MeshReceiver : IDisposable
    {
        const float RetryS = 3f;

        readonly ReconstructionSource _source;
        readonly ChunkMeshStore _store;
        readonly ReconstructionStatus _status;
        readonly IClock _clock;
        readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        int _epoch = -1;

        public MeshReceiver(ReconstructionSource source, ChunkMeshStore store, ReconstructionStatus status, IClock clock)
        {
            _source = source;
            _store = store;
            _status = status;
            _clock = clock;
        }

        public async void Start()
        {
            try
            {
                if (_source.Mode == ReconstructionMode.Live) await ReceiveLive(_cancel.Token);
                else if (_source.Mode == ReconstructionMode.Replay) await ReplayFromDisk(_cancel.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogException(e);
                _status.Fail(e.Message);
            }
        }

        public void Dispose() => _cancel.Cancel();

        /// <summary>Live path: connect, apply updates in order, and on any failure retry after a pause.</summary>
        async Awaitable ReceiveLive(CancellationToken token)
        {
            var uri = new Uri(_source.Url);
            while (!token.IsCancellationRequested)
            {
                // While retrying after a failure the row stays red rather than flickering through "connecting".
                if (_status.Link != LinkState.Failed) _status.Link = LinkState.Connecting;
                using (var socket = new ClientWebSocket())
                {
                    string reason;
                    try
                    {
                        await socket.ConnectAsync(uri, token);
                        _status.Link = LinkState.Connected;
                        Debug.Log($"[View3D] receiving chunks from {uri}");
                        var receiving = Task.Run(() => ReceiveLoop(socket, token));      // socket -> inbox
                        while (!receiving.IsCompleted || !_inbox.IsEmpty)                 // inbox -> ApplyUpdate, one at a time
                        {
                            if (_inbox.TryDequeue(out byte[] message)) await ApplyUpdate(message);
                            else await Awaitable.NextFrameAsync(token);
                        }
                        reason = receiving.Result;
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        reason = e.GetBaseException().Message;
                    }
                    if (token.IsCancellationRequested) return;
                    _status.Fail(reason);
                    Debug.LogWarning($"[View3D] {uri}: {reason}; retrying in {RetryS:0} s");
                }
                await Awaitable.WaitForSecondsAsync(RetryS, token);
            }
        }

        /// <summary>
        /// Reassembles WebSocket messages — one per chunk update — and hands each to the pump above.
        /// One message can arrive in several frames, so bytes accumulate until the message ends.
        /// Returns why the connection ended.
        /// </summary>
        async Task<string> ReceiveLoop(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[1 << 20];
            using var assembly = new MemoryStream();
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close) return "the service closed the connection";
                    assembly.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        _inbox.Enqueue(assembly.ToArray());
                        assembly.SetLength(0);
                    }
                }
                return "the connection closed";
            }
            catch (OperationCanceledException) { return "stopped"; }
            catch (Exception e) { return e.GetBaseException().Message; }
        }

        /// <summary>Staging path: replay .cgzm files from disk, paced so the build-up is watchable.</summary>
        async Awaitable ReplayFromDisk(CancellationToken token)
        {
            string dir = _source.StagingDir;
            if (!Directory.Exists(dir))
            {
                _status.Fail($"{dir} does not exist");
                return;
            }
            var files = Directory.GetFiles(dir, "*.cgzm");
            if (files.Length == 0)
            {
                _status.Fail($"no .cgzm files in {dir}");
                return;
            }
            Array.Sort(files, StringComparer.Ordinal);
            _status.UpdatesTotal = files.Length;
            _status.Link = LinkState.Replaying;
            Debug.Log($"[View3D] replaying {files.Length} updates from {dir}");

            foreach (var file in files)
            {
                await ApplyUpdate(File.ReadAllBytes(file));
                if (_source.ReplayIntervalS > 0f) await Awaitable.WaitForSecondsAsync(_source.ReplayIntervalS, token);
                else await Awaitable.NextFrameAsync(token);
            }
            _status.Link = LinkState.ReplayDone;
            Debug.Log($"[View3D] replay done: {_store.Count} chunks");
        }

        /// <summary>
        /// Applies one update. Disk replay and the WebSocket share this method; only the source of
        /// <paramref name="data"/> differs. The layout is parsed by <see cref="ChunkUpdate.Parse"/>.
        /// </summary>
        async Awaitable ApplyUpdate(byte[] data)
        {
            ChunkUpdate update;
            try
            {
                update = ChunkUpdate.Parse(data);
            }
            catch (InvalidDataException e)
            {
                _status.DecodeFailures++;
                Debug.LogWarning($"[View3D] dropped an update: {e.Message}");
                return;
            }

            // A new epoch means the worker's tracking relocated; everything placed is now in a
            // stale frame, so drop it and start the new one clean.
            if (update.Epoch != _epoch)
            {
                _store.Clear();
                _status.SetChunks(0);
                _epoch = update.Epoch;
            }
            foreach (var key in update.Removed) _store.Remove(key);

            for (int n = 0; n < update.Replaced.Length; n++)
            {
                // The Mesh-returning overload wants the data as a NativeArray; await keeps the
                // main-thread start and the return for the upload, and yields null on a decode error.
                using var encoded = new NativeArray<byte>(update.Payloads[n], Allocator.Persistent);
                Mesh mesh = await DracoDecoder.DecodeMesh(encoded.AsReadOnly());
                if (_cancel.IsCancellationRequested)
                {
                    if (mesh != null) UnityEngine.Object.Destroy(mesh);
                    return;
                }
                if (mesh != null) _store.Upsert(update.Replaced[n], ToUnityFrame(mesh));
                else _status.DecodeFailures++;
            }
            _status.Applied(_store.Count, _clock.NowNs);
        }

        /// <summary>
        /// A point from the server's frame into Unity's. Negating Y is the OpenCV↔Unity convention
        /// (<see cref="CameraGeometry.FlipY"/>); negating X on top of it corrects a left-right flip that
        /// the reconstruction genuinely has — confirmed against the real room, a bottle to the left of
        /// a paper coming out on the wrong side. It cannot be fixed in FlipY: that convention is the
        /// textbook one and cv2's rectification needs the server frame right-handed, so the flip enters
        /// the reconstruction itself and is undone here. Whatever projects gaze into this geometry, or
        /// places the worker's pose in it, must use this same conversion, or they and the mesh will
        /// disagree (CLAUDE.md, "A left-right flip in the reconstruction"). It is its own inverse.
        /// </summary>
        public static Vector3 ToUnityPoint(Vector3 server)
        {
            var v = CameraGeometry.FlipY(server);
            v.x = -v.x;
            return v;
        }

        /// <summary>
        /// Brings a decoded chunk into Unity's frame with <see cref="ToUnityPoint"/>. Bounds are
        /// recomputed because the vertices moved; the material is unlit, so no normals are needed.
        /// </summary>
        static Mesh ToUnityFrame(Mesh mesh)
        {
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = ToUnityPoint(vertices[i]);
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
