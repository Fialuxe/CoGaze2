using System;
using System.Collections.Generic;
using System.Linq;
using CoGaze.Core;
using CoGaze.Protocol;
using CoGaze.Transport.WebRtc;
using NUnit.Framework;

namespace CoGaze.Shared.Tests
{
    /// <summary>The engine-free half of Transport.WebRtc: frame-id strip, signaling format, link reporting.</summary>
    public class WebRtcPureTests
    {
        sealed class Rows : ILogSink
        {
            public readonly List<string> All = new List<string>();
            public void Write(string table, string row) => All.Add(table + ":" + row);
        }

        sealed class Board : IHealthBoard
        {
            readonly Dictionary<string, HealthItem> _items = new Dictionary<string, HealthItem>();
            public void Set(in HealthItem item) => _items[item.Key] = item;
            public IReadOnlyList<HealthItem> Items => _items.Values.ToList();
            public bool AllOk => _items.Values.All(i => i.Level == HealthLevel.Ok);
            public HealthItem this[string key] => _items[key];
        }

        const int W = 1280;

        static byte[] Strip(uint id)
        {
            var rgba = new byte[W * FrameIdStrip.HeightPx * 4];
            FrameIdStrip.Paint(id, rgba, W);
            return rgba;
        }

        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(123456u)]
        [TestCase(uint.MaxValue)]
        public void FrameId_RoundTrips(uint id)
        {
            Assert.IsTrue(FrameIdStrip.TryRead(Strip(id), W, FrameIdStrip.HeightPx, out uint back));
            Assert.AreEqual(id, back);
        }

        [Test]
        public void FrameId_SurvivesNoiseBlurAndLowContrast()
        {
            var rgba = Strip(987654321u);
            var rng = new Random(7);
            var blurred = (byte[])rgba.Clone();
            for (int y = 0; y < FrameIdStrip.HeightPx; y++)
                for (int x = 0; x < W; x++)
                    for (int ch = 0; ch < 3; ch++)
                    {
                        // 5-px horizontal box blur, contrast squeezed into 60..190, +-30 noise: worse than H.264 at a sane bitrate.
                        int sum = 0, n = 0;
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int xx = Math.Min(W - 1, Math.Max(0, x + dx));
                            sum += rgba[(y * W + xx) * 4 + ch];
                            n++;
                        }
                        double v = 60 + (sum / (double)n) * 130 / 255 + rng.Next(-30, 31);
                        blurred[(y * W + x) * 4 + ch] = (byte)Math.Max(0, Math.Min(255, v));
                    }
            Assert.IsTrue(FrameIdStrip.TryRead(blurred, W, FrameIdStrip.HeightPx, out uint back));
            Assert.AreEqual(987654321u, back);
        }

        [Test]
        public void FrameId_RejectsFlatImageAndFlippedBit()
        {
            var grey = Enumerable.Repeat((byte)128, W * FrameIdStrip.HeightPx * 4).ToArray();
            Assert.IsFalse(FrameIdStrip.TryRead(grey, W, FrameIdStrip.HeightPx, out _), "no marker contrast");

            var rgba = Strip(42u);
            int cw = FrameIdStrip.CellWidth(W);
            int cell = 4 + 31;   // least significant id bit
            for (int y = 0; y < FrameIdStrip.HeightPx; y++)
                for (int x = cell * cw; x < (cell + 1) * cw; x++)
                    for (int ch = 0; ch < 3; ch++) rgba[(y * W + x) * 4 + ch] ^= 0xFF;
            Assert.IsFalse(FrameIdStrip.TryRead(rgba, W, FrameIdStrip.HeightPx, out _), "check bits catch a wrong bit");
        }

        [Test]
        public void FrameId_RejectsMirroredStrip()
        {
            var rgba = Strip(5u);
            var mirrored = new byte[rgba.Length];
            for (int y = 0; y < FrameIdStrip.HeightPx; y++)
                for (int x = 0; x < W; x++)
                    Array.Copy(rgba, (y * W + x) * 4, mirrored, (y * W + (W - 1 - x)) * 4, 4);
            Assert.IsFalse(FrameIdStrip.TryRead(mirrored, W, FrameIdStrip.HeightPx, out _));
        }

        [Test]
        public void SignalMessages_RoundTripThroughJson()
        {
            var sdp = "v=0\r\no=- 46117 2 IN IP4 127.0.0.1\r\na=group:BUNDLE 0 1\r\n";
            Assert.IsTrue(SignalMessage.TryParse(SignalMessage.OfOffer(sdp).ToJson(), out var offer));
            Assert.AreEqual(SignalMessage.Offer, offer.Type);
            Assert.AreEqual(sdp, offer.Sdp);

            Assert.IsTrue(SignalMessage.TryParse(SignalMessage.OfAnswer("a").ToJson(), out var answer));
            Assert.AreEqual(SignalMessage.Answer, answer.Type);

            var line = "candidate:1 1 udp 2122260223 192.168.0.2 50000 typ host";
            Assert.IsTrue(SignalMessage.TryParse(SignalMessage.OfCandidate(line, "0", 1).ToJson(), out var cand));
            Assert.AreEqual(line, cand.CandidateLine);
            Assert.AreEqual("0", cand.SdpMid);
            Assert.AreEqual(1, cand.SdpMLineIndex);
        }

        [Test]
        public void SignalMessages_ReadTheRelaysPresence_AndRejectJunk()
        {
            Assert.IsTrue(SignalMessage.TryParse("{\"type\": \"peer\", \"present\": true}", out var p));
            Assert.AreEqual(SignalMessage.Peer, p.Type);
            Assert.IsTrue(p.PeerPresent);
            foreach (var junk in new[] { "", "not json", "[]", "{}", "{\"type\":\"offer\"}", "{\"type\":3}", "{\"type\":\"hello\"}" })
                Assert.IsFalse(SignalMessage.TryParse(junk, out _), junk);
        }

        [Test]
        public void Backoff_DoublesToTheCap_AndResets()
        {
            var b = new Backoff(500, 5000);
            CollectionAssert.AreEqual(new[] { 500, 1000, 2000, 4000, 5000, 5000 }, Enumerable.Range(0, 6).Select(_ => b.NextMs()));
            b.Reset();
            Assert.AreEqual(500, b.NextMs());
        }

        [Test]
        public void RelayUrl_AppendsTheRole()
        {
            Assert.AreEqual("ws://h:8765/signal/expert", LinkRoles.UrlFor("ws://h:8765/signal/", LinkRole.Expert));
            Assert.AreEqual("ws://h:8765/signal/worker", LinkRoles.UrlFor("ws://h:8765/signal", LinkRole.Worker));
        }

        [Test]
        public void LinkMonitor_LogsEachOutageWithItsLength_AndShowsItOnTheChecklist()
        {
            var log = new Rows();
            var board = new Board();
            var m = new LinkMonitor(log, board, LinkRole.Expert, "ws://relay/signal/expert");

            m.Update(LinkState.SignalingDown, 0);
            Assert.AreEqual(HealthLevel.Error, board[LinkMonitor.HealthKey].Level);
            StringAssert.Contains("ws://relay", board[LinkMonitor.HealthKey].Problem);
            m.Update(LinkState.WaitingForPeer, 10);
            StringAssert.Contains("worker", board[LinkMonitor.HealthKey].Problem);
            m.Update(LinkState.Connecting, 20);
            Assert.AreEqual(HealthLevel.Warning, board[LinkMonitor.HealthKey].Level);
            Assert.IsEmpty(log.All, "nothing to report before the first connection");

            m.Update(LinkState.Connected, 1_000_000_000);
            m.Update(LinkState.Connected, 1_100_000_000);
            Assert.AreEqual(HealthLevel.Ok, board[LinkMonitor.HealthKey].Level);
            m.Update(LinkState.Connecting, 2_000_000_000);
            m.Update(LinkState.WaitingForPeer, 2_500_000_000);
            m.Update(LinkState.Connected, 4_500_000_000);

            CollectionAssert.AreEqual(new[]
            {
                "events:1000000000,link_up,first",
                "events:2000000000,link_down,state=Connecting",
                "events:4500000000,link_up,gap_ms=2500",
            }, log.All);
        }

        [Test]
        public void LinkMonitor_WorksWithoutABoard()
        {
            var log = new Rows();
            var m = new LinkMonitor(log, null, LinkRole.Worker, "ws://relay/signal/worker");
            m.Update(LinkState.Connected, 5);
            m.Update(LinkState.SignalingDown, 6);
            Assert.AreEqual(2, log.All.Count);
        }

        [Test]
        public void Outbox_HoldsStateChangesInOrder_DropsTimeStampedOnes()
        {
            var box = new ControlOutbox(limit: 2);
            Assert.IsTrue(box.Hold(ControlMessage.Session("P01_x")));
            Assert.IsFalse(box.Hold(ControlMessage.Of(new ClockPing(1, 5))));
            Assert.IsFalse(box.Hold(ControlMessage.Of(new WorkerStatus(72f, AlignmentState.Aligned, true, 5))));
            Assert.IsTrue(box.Hold(ControlMessage.Ack("C01")));
            Assert.IsTrue(box.Hold(ControlMessage.Ack("C02")));
            Assert.AreEqual(1, box.Dropped, "oldest dropped past the limit");

            var kinds = new List<string>();
            while (box.TryTake(out var bytes))
            {
                Assert.IsTrue(MessageCodec.TryDecode(bytes, out ControlMessage m));
                kinds.Add(m.Text);
            }
            CollectionAssert.AreEqual(new[] { "C01", "C02" }, kinds);
        }

        [Test]
        public void VideoHealth_RedWithoutVideoOrIds_GreenWhenIdsRead()
        {
            var board = new Board();
            var r = new VideoHealthReporter(board);
            r.Tick(false, 0, 0, 0, 0);
            r.Tick(false, 0, 0, 0, 1_000_000_000);
            Assert.AreEqual(HealthLevel.Error, board[VideoHealthReporter.HealthKey].Level);

            r.Tick(true, 0, 30, 0, 2_000_000_000);
            Assert.AreEqual(HealthLevel.Error, board[VideoHealthReporter.HealthKey].Level);
            StringAssert.Contains("ids unreadable", board[VideoHealthReporter.HealthKey].Value);

            r.Tick(true, 100, 31, 30, 3_000_000_000);
            Assert.AreEqual(HealthLevel.Ok, board[VideoHealthReporter.HealthKey].Level);
            Assert.AreEqual("30 fps", board[VideoHealthReporter.HealthKey].Value);
        }
    }
}
