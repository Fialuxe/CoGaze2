using System;
using System.Net;
using System.Net.Sockets;
using CoGaze.Core;

namespace CoGaze.Expert.Sources.Osc
{
    /// <summary>Sends /cogaze/control start|stop to the recording and estimation processes (EXP-F-005).</summary>
    public sealed class OscControlSender : IDisposable
    {
        readonly UdpClient _udp = new UdpClient();
        readonly int[] _ports;
        readonly IClock _clock;

        public OscControlSender(int[] ports, IClock clock)
        {
            _ports = ports;
            _clock = clock;
        }

        public void Start(string sessionDir) => Send("start", sessionDir);
        public void Stop(string sessionDir) => Send("stop", sessionDir);

        void Send(string command, string sessionDir)
        {
            byte[] packet = OscMessage.Encode(OscAddresses.Control, command, sessionDir, _clock.NowNs);
            foreach (int port in _ports)
                _udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));
        }

        public void Dispose() => _udp.Dispose();
    }
}
