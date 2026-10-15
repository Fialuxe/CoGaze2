using System.Collections.Generic;
using CoGaze.Expert.View3D.Model;
using UnityEngine;

namespace CoGaze.Expert.View3D
{
    /// <summary>
    /// The reconstruction in the scene: one GameObject per chunk under a single root at the world
    /// origin, so chunk vertices are world positions. Every chunk carries a MeshCollider, so gaze in
    /// 3D (M4) can Physics.Raycast from the expert's camera into the mesh without further plumbing.
    /// </summary>
    sealed class ChunkMeshStore
    {
        readonly Dictionary<ChunkKey, (GameObject go, Mesh mesh)> _chunks = new Dictionary<ChunkKey, (GameObject, Mesh)>();
        readonly Material _material;

        public Transform Root { get; }
        public int Count => _chunks.Count;

        public ChunkMeshStore(Transform parent, Material material)
        {
            Root = new GameObject("Reconstruction").transform;
            Root.SetParent(parent, false);
            _material = material;
        }

        public void Upsert(ChunkKey key, Mesh mesh)
        {
            if (_chunks.TryGetValue(key, out var existing))
            {
                Object.Destroy(existing.mesh);
                existing.go.GetComponent<MeshFilter>().sharedMesh = mesh;
                existing.go.GetComponent<MeshCollider>().sharedMesh = mesh;
                _chunks[key] = (existing.go, mesh);
                return;
            }

            var go = new GameObject(key.ToString());
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            _chunks[key] = (go, mesh);
        }

        public void Remove(ChunkKey key)
        {
            if (!_chunks.TryGetValue(key, out var existing)) return;
            Object.Destroy(existing.mesh);
            Object.Destroy(existing.go);
            _chunks.Remove(key);
        }

        public void Clear()
        {
            foreach (var (go, mesh) in _chunks.Values)
            {
                Object.Destroy(mesh);
                Object.Destroy(go);
            }
            _chunks.Clear();
        }

        /// <summary>World-space box around every chunk; false while there are none.</summary>
        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var (_, mesh) in _chunks.Values)
            {
                if (!any) bounds = mesh.bounds;
                else bounds.Encapsulate(mesh.bounds);
                any = true;
            }
            return any;
        }
    }
}
