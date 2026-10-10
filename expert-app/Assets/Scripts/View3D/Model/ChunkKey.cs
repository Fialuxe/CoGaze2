using System;

namespace CoGaze.Expert.View3D.Model
{
    /// <summary>A chunk's level of detail (0 near, 1 far, chunks.LOD_CODES) and integer position in that grid.</summary>
    public readonly struct ChunkKey : IEquatable<ChunkKey>
    {
        public readonly byte Lod;
        public readonly int I, J, K;

        public ChunkKey(byte lod, int i, int j, int k)
        {
            Lod = lod;
            I = i;
            J = j;
            K = k;
        }

        public bool Equals(ChunkKey o) => Lod == o.Lod && I == o.I && J == o.J && K == o.K;
        public override bool Equals(object obj) => obj is ChunkKey o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Lod, I, J, K);

        /// <summary>Also the chunk's GameObject name.</summary>
        public override string ToString() => $"{(Lod == 0 ? "near" : "far")}_{I}_{J}_{K}";
    }
}
