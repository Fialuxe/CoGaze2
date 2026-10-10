using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Draco;
using Unity.Collections;
using CoGaze.Geometry;
using UnityEngine;

namespace CoGaze.Expert.View3D
{
    /// <summary>
    /// Expert side: rebuilds the worker's space from chunk updates and keeps it current as chunks
    /// change.
    ///
    /// Two sources feed the same <see cref="ApplyUpdate"/>. With <c>serverUrl</c> set it receives
    /// live from <c>service.py</c> over a WebSocket; empty, it replays the .cgzm files written by
    /// <c>server/run_chunks.py</c> from disk — the staging path that isolates whether Unity renders
    /// our geometry in the right place from whether the transport works. The bytes are identical
    /// either way, so switching source changes nothing downstream.
    /// </summary>
    public class MeshReceiver : MonoBehaviour
    {
        [Tooltip("Reconstruction service, e.g. ws://192.168.1.10:8765/expert. Empty replays from disk.")]
        [SerializeField] string serverUrl = "";
        [Tooltip("Directory of .cgzm files to replay when serverUrl is empty. " +
                 "Blank uses <repo>/recordings/session01_chunks.")]
        [SerializeField] string stagingDirectory = "";
        [Tooltip("Pause between updates so the reconstruction can be watched building up. " +
                 "0 applies them as fast as they decode.")]
        [SerializeField] float secondsBetweenUpdates = 0.15f;
        [Tooltip("Material for the chunk meshes. Assign a vertex-colour material to see the captured " +
                 "colour and confidence (alpha); left empty, a plain lit material shows shape only.")]
        [SerializeField] Material material;

        // A chunk is identified by its level of detail and its integer position in that grid.
        readonly Dictionary<(int lod, int i, int j, int k), (GameObject go, Mesh mesh)> _chunks = new();
        readonly ConcurrentQueue<byte[]> _inbox = new();
        Transform _root;
        int _epoch = -1;
        ClientWebSocket _socket;
        CancellationTokenSource _cancel;

        async void Start()
        {
            _root = new GameObject("Reconstruction").transform;
            _root.SetParent(transform, false);
            if (material == null) material = DefaultMaterial();

            if (string.IsNullOrEmpty(serverUrl))
                await ReplayFromDisk();
            else
                await ReceiveLive();
        }

        void OnDisable()
        {
            _cancel?.Cancel();
            _socket?.Dispose();
        }

        /// <summary>Live path: receive chunk updates from the service and apply them in order.</summary>
        async Task ReceiveLive()
        {
            _cancel = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            try
            {
                await _socket.ConnectAsync(new Uri(serverUrl), _cancel.Token);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EnvLine] could not connect to {serverUrl}: {e.Message}. Disabling.");
                return;
            }
            Debug.Log($"[EnvLine] receiving chunks from {serverUrl}");

            _ = Task.Run(() => ReceiveLoop());          // socket -> inbox
            while (!_cancel.IsCancellationRequested)     // inbox -> ApplyUpdate, one at a time
            {
                if (_inbox.TryDequeue(out byte[] message))
                    await ApplyUpdate(message);
                else
                    await Awaitable.NextFrameAsync(_cancel.Token);
            }
        }

        /// <summary>
        /// Reassembles WebSocket messages — one per chunk update — and hands each to the pump above.
        /// One message can arrive in several frames, so bytes accumulate until the message ends.
        /// </summary>
        async Task ReceiveLoop()
        {
            var buffer = new byte[1 << 20];
            using var assembly = new MemoryStream();
            while (!_cancel.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancel.Token);
                }
                catch (OperationCanceledException) { break; }
                if (result.MessageType == WebSocketMessageType.Close) break;

                assembly.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    _inbox.Enqueue(assembly.ToArray());
                    assembly.SetLength(0);
                }
            }
        }

        /// <summary>Staging path: replay .cgzm files from disk, paced so the build-up is watchable.</summary>
        async Task ReplayFromDisk()
        {
            string dir = string.IsNullOrEmpty(stagingDirectory)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "recordings", "session01_chunks"))
                : stagingDirectory;
            if (!Directory.Exists(dir))
            {
                Debug.LogError($"[EnvLine] no chunk directory at {dir}. Disabling.");
                return;
            }

            var files = Directory.GetFiles(dir, "*.cgzm");
            Array.Sort(files, StringComparer.Ordinal);
            Debug.Log($"[EnvLine] replaying {files.Length} updates from {dir}");

            foreach (var file in files)
            {
                await ApplyUpdate(File.ReadAllBytes(file));
                if (secondsBetweenUpdates > 0f)
                    await Awaitable.WaitForSecondsAsync(secondsBetweenUpdates);
            }
            Debug.Log($"[EnvLine] done: {_chunks.Count} chunks live");
        }

        /// <summary>
        /// Applies one update. Disk staging and the eventual WebSocket share this method; only the
        /// source of <paramref name="data"/> differs. The byte layout mirrors
        /// <c>chunks.Update.encode</c> and is little-endian, which is what BinaryReader reads.
        /// </summary>
        public async Task ApplyUpdate(byte[] data)
        {
            var reader = new BinaryReader(new MemoryStream(data));
            reader.ReadBytes(4);                          // magic
            reader.ReadUInt16();                          // version
            int epoch = reader.ReadUInt16();
            int replacedCount = (int)reader.ReadUInt32();
            int removedCount = (int)reader.ReadUInt32();

            // A new epoch means the worker's tracking relocated; everything placed is now in a
            // stale frame, so drop it and start the new one clean.
            if (epoch != _epoch)
            {
                ClearAll();
                _epoch = epoch;
            }

            // Keys for both lists come before any payload, so they are read up front.
            var replaced = new (int lod, int i, int j, int k)[replacedCount];
            for (int n = 0; n < replacedCount; n++) replaced[n] = ReadKey(reader);
            for (int n = 0; n < removedCount; n++) Remove(ReadKey(reader));

            for (int n = 0; n < replacedCount; n++)
            {
                int payloadLength = (int)reader.ReadUInt32();
                int labelLength = (int)reader.ReadUInt32();
                byte[] payload = reader.ReadBytes(payloadLength);
                reader.ReadBytes(labelLength);            // labels: carried but unused until semantics land

                // The Mesh-returning overload wants the data as a NativeArray; await keeps the
                // main-thread start and the return for the upload, and yields null on a decode error.
                using var encoded = new NativeArray<byte>(payload, Allocator.Persistent);
                Mesh mesh = await DracoDecoder.DecodeMesh(encoded.AsReadOnly());
                if (mesh != null) Upsert(replaced[n], ToUnityFrame(mesh));
            }
        }

        static (int lod, int i, int j, int k) ReadKey(BinaryReader reader)
            => (reader.ReadByte(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

        /// <summary>
        /// Brings a decoded chunk from the server's frame into Unity's. Negating Y is the OpenCV↔Unity
        /// convention (<see cref="CameraGeometry.FlipY"/>); negating X on top of it corrects a
        /// left-right flip that the reconstruction genuinely has — confirmed against the real room, a
        /// bottle to the left of a paper coming out on the wrong side. It cannot be fixed in FlipY:
        /// that convention is the textbook one and cv2's rectification needs the server frame
        /// right-handed, so the flip enters the reconstruction itself and is undone here. Whatever
        /// projects gaze into this geometry later must apply the same X negation, or gaze and mesh
        /// will disagree. Bounds are recomputed because the vertices moved; the material is unlit, so
        /// no normals are needed.
        /// </summary>
        static Mesh ToUnityFrame(Mesh mesh)
        {
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = CameraGeometry.FlipY(vertices[i]);
                v.x = -v.x;
                vertices[i] = v;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            return mesh;
        }

        void Upsert((int lod, int i, int j, int k) key, Mesh mesh)
        {
            if (_chunks.TryGetValue(key, out var existing))
            {
                Destroy(existing.mesh);
                existing.go.GetComponent<MeshFilter>().sharedMesh = mesh;
                _chunks[key] = (existing.go, mesh);
                return;
            }

            var go = new GameObject($"{(key.lod == 0 ? "near" : "far")}_{key.i}_{key.j}_{key.k}");
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            _chunks[key] = (go, mesh);
        }

        void Remove((int lod, int i, int j, int k) key)
        {
            if (!_chunks.TryGetValue(key, out var existing)) return;
            Destroy(existing.mesh);
            Destroy(existing.go);
            _chunks.Remove(key);
        }

        void ClearAll()
        {
            foreach (var (go, mesh) in _chunks.Values)
            {
                Destroy(mesh);
                Destroy(go);
            }
            _chunks.Clear();
        }

        static Material DefaultMaterial()
        {
            var shader = Shader.Find("CoGaze2/VertexColorUnlit")
                ?? Shader.Find("Universal Render Pipeline/Unlit");
            return new Material(shader);
        }
    }
}
