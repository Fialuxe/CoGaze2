using System;
using System.IO;

namespace CoGaze.Expert.View3D.Model
{
    /// <summary>
    /// One chunk update as the server sends it (server/chunks.py <c>Update.encode</c>, CLAUDE.md "Wire
    /// formats"): the chunks that changed, each with its Draco payload, and the chunks that no longer
    /// hold a surface. Parsing only; decoding the payloads is Unity's (Draco) and lives in MeshReceiver.
    /// </summary>
    public sealed class ChunkUpdate
    {
        public const string Magic = "CGZM";

        public int Epoch { get; }
        public ChunkKey[] Replaced { get; }
        /// <summary>Draco-compressed mesh per entry of <see cref="Replaced"/>, same order.</summary>
        public byte[][] Payloads { get; }
        public ChunkKey[] Removed { get; }

        ChunkUpdate(int epoch, ChunkKey[] replaced, byte[][] payloads, ChunkKey[] removed)
        {
            Epoch = epoch;
            Replaced = replaced;
            Payloads = payloads;
            Removed = removed;
        }

        /// <summary>
        /// Little-endian, which is what BinaryReader reads: "&lt;4sHHII" header, then every key
        /// ("&lt;Biii") — replaced first, removed after — then per replaced chunk "&lt;II" lengths, the
        /// payload and the labels (carried but unused until semantics land).
        /// </summary>
        /// <exception cref="InvalidDataException">Not a chunk update, or cut short.</exception>
        public static ChunkUpdate Parse(byte[] data)
        {
            try
            {
                var reader = new BinaryReader(new MemoryStream(data));
                var magic = reader.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != 'C' || magic[1] != 'G' || magic[2] != 'Z' || magic[3] != 'M')
                    throw new InvalidDataException($"not a chunk update (expected {Magic})");
                reader.ReadUInt16();                                  // version
                int epoch = reader.ReadUInt16();
                int replacedCount = checked((int)reader.ReadUInt32());
                int removedCount = checked((int)reader.ReadUInt32());

                var replaced = new ChunkKey[replacedCount];
                for (int n = 0; n < replacedCount; n++) replaced[n] = ReadKey(reader);
                var removed = new ChunkKey[removedCount];
                for (int n = 0; n < removedCount; n++) removed[n] = ReadKey(reader);

                var payloads = new byte[replacedCount][];
                for (int n = 0; n < replacedCount; n++)
                {
                    int payloadLength = checked((int)reader.ReadUInt32());
                    int labelLength = checked((int)reader.ReadUInt32());
                    payloads[n] = ReadExactly(reader, payloadLength);
                    ReadExactly(reader, labelLength);
                }
                return new ChunkUpdate(epoch, replaced, payloads, removed);
            }
            catch (Exception e) when (e is EndOfStreamException || e is OverflowException)
            {
                throw new InvalidDataException("chunk update is cut short", e);
            }
        }

        static ChunkKey ReadKey(BinaryReader r) => new ChunkKey(r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());

        static byte[] ReadExactly(BinaryReader r, int count)
        {
            var bytes = r.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException();
            return bytes;
        }
    }
}
