using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoGaze.Transport.WebRtc
{
    /// <summary>
    /// WebSocket client to the signaling relay. Connects on a background task and reconnects with
    /// backoff for as long as it lives; the main thread only enqueues and polls. Messages sent while
    /// the socket is down are dropped: negotiation restarts from the next peer-presence message, so
    /// a stale offer would only confuse it. Outgoing order is preserved (an offer before its candidates).
    /// </summary>
    public sealed class SignalingClient : IDisposable
    {
        const int ConnectTimeoutMs = 3000;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        readonly Uri _uri;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly ConcurrentQueue<SignalMessage> _in = new ConcurrentQueue<SignalMessage>();
        readonly ConcurrentQueue<byte[]> _out = new ConcurrentQueue<byte[]>();
        readonly SemaphoreSlim _outReady = new SemaphoreSlim(0);
        volatile ClientWebSocket _open;
        volatile string _lastError;
        int _connects;

        public SignalingClient(string url)
        {
            _uri = new Uri(url);
        }

        public string Url => _uri.ToString();
        public bool IsOpen => _open != null;
        /// <summary>Why the last connection attempt or connection ended; null when none failed.</summary>
        public string LastError => _lastError;
        /// <summary>Successful connections so far; a change means the relay saw us anew.</summary>
        public int Connects => Volatile.Read(ref _connects);

        public void Start() => Task.Run(RunAsync);

        public void Send(in SignalMessage message)
        {
            if (_open == null) return;
            _out.Enqueue(Utf8.GetBytes(message.ToJson()));
            _outReady.Release();
        }

        public bool TryReceive(out SignalMessage message) => _in.TryDequeue(out message);

        public void Dispose()
        {
            _cts.Cancel();
            try { _open?.Abort(); }
            catch (ObjectDisposedException) { }
        }

        async Task RunAsync()
        {
            var backoff = new Backoff();
            while (!_cts.IsCancellationRequested)
            {
                using (var ws = new ClientWebSocket())
                {
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                    try
                    {
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                        {
                            timeout.CancelAfter(ConnectTimeoutMs);
                            await ws.ConnectAsync(_uri, timeout.Token).ConfigureAwait(false);
                        }
                        while (_out.TryDequeue(out _)) { }
                        _open = ws;
                        Interlocked.Increment(ref _connects);
                        backoff.Reset();
                        _lastError = null;
                        using (var connection = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                        {
                            var receive = ReceiveLoop(ws, connection.Token);
                            var send = SendLoop(ws, connection.Token);
                            await Task.WhenAny(receive, send).ConfigureAwait(false);
                            _open = null;
                            connection.Cancel();
                            await Task.WhenAll(receive, send).ConfigureAwait(false);
                        }
                        _lastError = _lastError ?? "connection closed";
                    }
                    catch (Exception e) when (IsConnectionError(e))
                    {
                        _lastError = e.Message;
                    }
                    finally
                    {
                        _open = null;
                    }
                }
                try { await Task.Delay(backoff.NextMs(), _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        static bool IsConnectionError(Exception e) =>
            e is WebSocketException || e is OperationCanceledException || e is IOException || e is ObjectDisposedException;

        async Task ReceiveLoop(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            using (var message = new MemoryStream())
            {
                try
                {
                    while (ws.State == WebSocketState.Open)
                    {
                        var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, r.Count);
                        if (!r.EndOfMessage) continue;
                        if (r.MessageType == WebSocketMessageType.Text &&
                            SignalMessage.TryParse(Utf8.GetString(message.GetBuffer(), 0, (int)message.Length), out var m))
                            _in.Enqueue(m);
                        message.SetLength(0);
                    }
                }
                catch (Exception e) when (IsConnectionError(e))
                {
                    _lastError = e.Message;
                }
            }
        }

        async Task SendLoop(ClientWebSocket ws, CancellationToken token)
        {
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    await _outReady.WaitAsync(token).ConfigureAwait(false);
                    while (_out.TryDequeue(out var bytes))
                        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token)
                            .ConfigureAwait(false);
                }
            }
            catch (Exception e) when (IsConnectionError(e))
            {
                _lastError = e.Message;
            }
        }
    }
}
