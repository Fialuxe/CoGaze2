using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// One message through the signaling relay (server/signaling.py), as JSON text:
    /// {"type":"offer"|"answer","sdp":...}, {"type":"candidate","candidate":...,"sdpMid":...,"sdpMLineIndex":...},
    /// and the relay's own {"type":"peer","present":bool}. The relay forwards text verbatim, so this
    /// class is the whole format.
    /// </summary>
    public readonly struct SignalMessage
    {
        public const string Offer = "offer";
        public const string Answer = "answer";
        public const string Candidate = "candidate";
        public const string Peer = "peer";

        public readonly string Type;
        public readonly string Sdp;
        public readonly string CandidateLine;
        public readonly string SdpMid;
        public readonly int SdpMLineIndex;
        public readonly bool PeerPresent;

        SignalMessage(string type, string sdp = null, string candidate = null, string sdpMid = null, int sdpMLineIndex = 0,
                      bool peerPresent = false)
        {
            Type = type;
            Sdp = sdp;
            CandidateLine = candidate;
            SdpMid = sdpMid;
            SdpMLineIndex = sdpMLineIndex;
            PeerPresent = peerPresent;
        }

        public static SignalMessage OfOffer(string sdp) => new SignalMessage(Offer, sdp: sdp);
        public static SignalMessage OfAnswer(string sdp) => new SignalMessage(Answer, sdp: sdp);
        public static SignalMessage OfCandidate(string candidate, string sdpMid, int sdpMLineIndex) =>
            new SignalMessage(Candidate, candidate: candidate, sdpMid: sdpMid, sdpMLineIndex: sdpMLineIndex);
        public static SignalMessage OfPeer(bool present) => new SignalMessage(Peer, peerPresent: present);

        public string ToJson()
        {
            var o = new JObject { ["type"] = Type };
            switch (Type)
            {
                case Offer:
                case Answer:
                    o["sdp"] = Sdp ?? "";
                    break;
                case Candidate:
                    o["candidate"] = CandidateLine ?? "";
                    o["sdpMid"] = SdpMid;
                    o["sdpMLineIndex"] = SdpMLineIndex;
                    break;
                case Peer:
                    o["present"] = PeerPresent;
                    break;
            }
            return o.ToString(Formatting.None);
        }

        /// <summary>False for anything that is not one of the four messages; never throws.</summary>
        public static bool TryParse(string json, out SignalMessage message)
        {
            message = default;
            JObject o;
            try { o = JObject.Parse(json); }
            catch (JsonException) { return false; }

            var type = o["type"];
            switch (type != null && type.Type == JTokenType.String ? (string)type : null)
            {
                case Offer when o["sdp"]?.Type == JTokenType.String:
                    message = OfOffer((string)o["sdp"]);
                    return true;
                case Answer when o["sdp"]?.Type == JTokenType.String:
                    message = OfAnswer((string)o["sdp"]);
                    return true;
                case Candidate when o["candidate"]?.Type == JTokenType.String:
                    var index = o["sdpMLineIndex"];
                    message = OfCandidate((string)o["candidate"], (string)o["sdpMid"],
                                          index != null && index.Type == JTokenType.Integer ? (int)index : 0);
                    return true;
                case Peer when o["present"]?.Type == JTokenType.Boolean:
                    message = OfPeer((bool)o["present"]);
                    return true;
                default:
                    return false;
            }
        }
    }
}
