using System;
using System.IO;
using System.Text;
using CoGaze.Core;

namespace CoGaze.Protocol
{
    /// <summary>
    /// Binary encoding of everything that crosses expert &lt;-&gt; worker. Layout: magic 0xC6, version,
    /// kind (0 = GazeCue, otherwise MessageKind), payload little-endian. Decoding never throws: a
    /// malformed message returns false so a bad packet cannot take the receive loop down.
    /// </summary>
    public static class MessageCodec
    {
        public const byte Magic = 0xC6;
        public const byte Version = 1;
        const byte CueKind = 0;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static byte[] Encode(in GazeCue c)
        {
            using (var ms = new MemoryStream(64))
            using (var w = new BinaryWriter(ms, Utf8))
            {
                Header(w, CueKind);
                w.Write(c.Seq);
                w.Write(c.Source ?? "");
                w.Write(c.Valid);
                w.Write((byte)c.Mode);
                w.Write(c.U);
                w.Write(c.V);
                w.Write(c.VideoFrameId);
                w.Write(c.SigmaDeg);
                w.Write(c.TExpNs);
                return ms.ToArray();
            }
        }

        public static bool TryDecode(byte[] data, out GazeCue cue)
        {
            cue = default;
            try
            {
                using (var r = Reader(data, out byte kind))
                {
                    if (r == null || kind != CueKind) return false;
                    cue = new GazeCue(r.ReadUInt32(), r.ReadString(), r.ReadBoolean(), (GazeMode)r.ReadByte(),
                                      r.ReadSingle(), r.ReadSingle(), r.ReadInt64(), r.ReadSingle(), r.ReadInt64());
                    return true;
                }
            }
            catch (Exception e) when (e is EndOfStreamException || e is IOException || e is ArgumentException)
            {
                return false;
            }
        }

        public static byte[] Encode(in ControlMessage m)
        {
            using (var ms = new MemoryStream(128))
            using (var w = new BinaryWriter(ms, Utf8))
            {
                Header(w, (byte)m.Kind);
                switch (m.Kind)
                {
                    case MessageKind.Condition:
                        var c = m.Condition;
                        w.Write(c.ConditionId ?? "");
                        w.Write(c.DisplaySource ?? "");
                        w.Write((byte)c.Visualization);
                        w.Write(c.ShowUncertainty);
                        w.Write((byte)c.SigmaSource);
                        w.Write(c.FixedSigmaDeg);
                        w.Write(c.Filter ?? "");
                        w.Write(c.Task ?? "");
                        break;
                    case MessageKind.ConditionAck:
                    case MessageKind.Session:
                        w.Write(m.Text ?? "");
                        break;
                    case MessageKind.Trial:
                        var t = m.Trial;
                        w.Write(t.TrialIndex);
                        w.Write((byte)t.Phase);
                        w.Write((byte)t.Kind);
                        w.Write(t.ConditionId ?? "");
                        w.Write(t.Task ?? "");
                        w.Write(t.LayoutJson ?? "");
                        break;
                    case MessageKind.WorkerStatus:
                        var s = m.Status;
                        w.Write(s.FrameRateHz);
                        w.Write((byte)s.Alignment);
                        w.Write(s.Recording);
                        w.Write(s.TWorkerNs);
                        break;
                    case MessageKind.WorkerEvent:
                        var e = m.Event;
                        w.Write(e.TWorkerNs);
                        w.Write(e.Kind ?? "");
                        w.Write(e.Detail ?? "");
                        break;
                    case MessageKind.ClockPing:
                        var p = m.Ping;
                        w.Write(p.Seq);
                        w.Write(p.TPingNs);
                        w.Write(p.TReceiveNs);
                        w.Write(p.TReplyNs);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(m), $"Unknown message kind {m.Kind}");
                }
                return ms.ToArray();
            }
        }

        public static bool TryDecode(byte[] data, out ControlMessage message)
        {
            message = default;
            try
            {
                using (var r = Reader(data, out byte kind))
                {
                    if (r == null) return false;
                    switch ((MessageKind)kind)
                    {
                        case MessageKind.Condition:
                            message = ControlMessage.Of(new Condition(r.ReadString(), r.ReadString(), (Visualization)r.ReadByte(),
                                r.ReadBoolean(), (SigmaSource)r.ReadByte(), r.ReadSingle(), r.ReadString(), r.ReadString()));
                            return true;
                        case MessageKind.ConditionAck:
                            message = ControlMessage.Ack(r.ReadString());
                            return true;
                        case MessageKind.Session:
                            message = ControlMessage.Session(r.ReadString());
                            return true;
                        case MessageKind.Trial:
                            message = ControlMessage.Of(new TrialMessage(r.ReadInt32(), (TrialPhase)r.ReadByte(),
                                (TrialKind)r.ReadByte(), r.ReadString(), r.ReadString(), r.ReadString()));
                            return true;
                        case MessageKind.WorkerStatus:
                            message = ControlMessage.Of(new WorkerStatus(r.ReadSingle(), (AlignmentState)r.ReadByte(),
                                r.ReadBoolean(), r.ReadInt64()));
                            return true;
                        case MessageKind.WorkerEvent:
                            message = ControlMessage.Of(new WorkerEvent(r.ReadInt64(), r.ReadString(), r.ReadString()));
                            return true;
                        case MessageKind.ClockPing:
                            message = ControlMessage.Of(new ClockPing(r.ReadUInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64()));
                            return true;
                        default:
                            return false;
                    }
                }
            }
            catch (Exception e) when (e is EndOfStreamException || e is IOException || e is ArgumentException)
            {
                return false;
            }
        }

        /// <summary>True when the bytes are a GazeCue rather than a control message.</summary>
        public static bool IsCue(byte[] data) => data != null && data.Length >= 3 && data[0] == Magic && data[2] == CueKind;

        static void Header(BinaryWriter w, byte kind)
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(kind);
        }

        static BinaryReader Reader(byte[] data, out byte kind)
        {
            kind = 0;
            if (data == null || data.Length < 3 || data[0] != Magic || data[1] != Version) return null;
            kind = data[2];
            var r = new BinaryReader(new MemoryStream(data, 3, data.Length - 3), Utf8);
            return r;
        }
    }
}
