using System;
using System.Collections.Generic;
using System.Text;

namespace CoGaze.Expert.Sources.Osc
{
    /// <summary>
    /// A decoded OSC message. Arguments keep their OSC type: h → long, i → int, f → float, s → string
    /// (SRS 3.2). Parsing returns false on anything malformed; it never throws.
    /// </summary>
    public sealed class OscMessage
    {
        public string Address { get; }
        public IReadOnlyList<object> Args { get; }

        public OscMessage(string address, IReadOnlyList<object> args)
        {
            Address = address;
            Args = args;
        }

        public long Long(int i) => Args[i] is long l ? l : Convert.ToInt64(Args[i]);
        public int Int(int i) => Args[i] is int v ? v : Convert.ToInt32(Args[i]);
        public float Float(int i) => Args[i] is float f ? f : Convert.ToSingle(Args[i]);
        public string Str(int i) => Args[i] as string ?? "";

        /// <summary>Parses a packet; bundles are flattened into their messages.</summary>
        public static bool TryParse(byte[] data, int length, List<OscMessage> into)
        {
            try
            {
                return Parse(data, 0, length, into);
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is ArgumentException || e is FormatException)
            {
                return false;
            }
        }

        static bool Parse(byte[] d, int start, int end, List<OscMessage> into)
        {
            int p = start;
            string head = ReadString(d, ref p, end);
            if (head == "#bundle")
            {
                p += 8; // timetag
                while (p + 4 <= end)
                {
                    int size = ReadInt(d, ref p);
                    if (size <= 0 || p + size > end) return false;
                    if (!Parse(d, p, p + size, into)) return false;
                    p += size;
                }
                return true;
            }
            if (head.Length == 0 || head[0] != '/') return false;
            string tags = p < end ? ReadString(d, ref p, end) : ",";
            if (tags.Length == 0 || tags[0] != ',') return false;
            var args = new List<object>(tags.Length - 1);
            for (int i = 1; i < tags.Length; i++)
            {
                switch (tags[i])
                {
                    case 'i': args.Add(ReadInt(d, ref p)); break;
                    case 'h': args.Add(ReadLong(d, ref p)); break;
                    case 'f': args.Add(BitConverter.Int32BitsToSingle(ReadInt(d, ref p))); break;
                    case 'd': args.Add((float)BitConverter.Int64BitsToDouble(ReadLong(d, ref p))); break;
                    case 's': args.Add(ReadString(d, ref p, end)); break;
                    case 'T': args.Add(1); break;
                    case 'F': args.Add(0); break;
                    default: return false;
                }
                if (p > end) return false;
            }
            into.Add(new OscMessage(head, args));
            return true;
        }

        static int ReadInt(byte[] d, ref int p)
        {
            int v = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
            p += 4;
            return v;
        }

        static long ReadLong(byte[] d, ref int p)
        {
            long hi = (uint)ReadInt(d, ref p);
            long lo = (uint)ReadInt(d, ref p);
            return (hi << 32) | lo;
        }

        static string ReadString(byte[] d, ref int p, int end)
        {
            int s = p;
            while (p < end && d[p] != 0) p++;
            if (p >= end) throw new FormatException("unterminated OSC string");
            string str = Encoding.UTF8.GetString(d, s, p - s);
            p = (p + 4) & ~3; // skip the terminator and pad to 4
            return str;
        }

        /// <summary>Encodes a message (used for /cogaze/control and in tests).</summary>
        public static byte[] Encode(string address, params object[] args)
        {
            var buf = new List<byte>(64);
            WriteString(buf, address);
            var tags = new StringBuilder(",");
            foreach (var a in args)
                tags.Append(a is long ? 'h' : a is int ? 'i' : a is float ? 'f' : a is string ? 's'
                    : throw new ArgumentException($"unsupported OSC argument {a?.GetType()}"));
            WriteString(buf, tags.ToString());
            foreach (var a in args)
            {
                switch (a)
                {
                    case long l: WriteInt(buf, (int)(l >> 32)); WriteInt(buf, (int)l); break;
                    case int i: WriteInt(buf, i); break;
                    case float f: WriteInt(buf, BitConverter.SingleToInt32Bits(f)); break;
                    case string s: WriteString(buf, s); break;
                }
            }
            return buf.ToArray();
        }

        static void WriteInt(List<byte> b, int v)
        {
            b.Add((byte)(v >> 24));
            b.Add((byte)(v >> 16));
            b.Add((byte)(v >> 8));
            b.Add((byte)v);
        }

        static void WriteString(List<byte> b, string s)
        {
            b.AddRange(Encoding.UTF8.GetBytes(s));
            int pad = 4 - (s.Length % 4);
            for (int i = 0; i < pad; i++) b.Add(0);
        }
    }
}
