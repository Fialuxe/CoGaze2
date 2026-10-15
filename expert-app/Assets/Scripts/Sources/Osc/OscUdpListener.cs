using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CoGaze.Expert.Sources.Osc
{
    /// <summary>Receives OSC on localhost UDP on a background thread and hands messages to the router.</summary>
    public sealed class OscUdpListener : IDisposable
    {
        readonly UdpClient _udp;
        readonly OscRouter _router;
        readonly Thread _thread;
        volatile bool _stop;

        public OscUdpListener(int port, OscRouter router)
        {
            _router = router;
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            _udp.Client.ReceiveTimeout = 200;
            _thread = new Thread(Run) { IsBackground = true, Name = "OscUdpListener" };
            _thread.Start();
        }

        /// <summary>Packets that failed to parse. Shown on the checklist.</summary>
        public int BadPackets { get; private set; }

        void Run()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            var batch = new List<OscMessage>();
            while (!_stop)
            {
                byte[] data;
                try { data = _udp.Receive(ref from); }
                catch (SocketException) { continue; }   // timeout: check _stop
                catch (ObjectDisposedException) { break; }
                batch.Clear();
                if (OscMessage.TryParse(data, data.Length, batch)) _router.Route(batch);
                else BadPackets++;
            }
        }

        public void Dispose()
        {
            _stop = true;
            _udp.Dispose();
            _thread.Join(500);
        }
    }
}
