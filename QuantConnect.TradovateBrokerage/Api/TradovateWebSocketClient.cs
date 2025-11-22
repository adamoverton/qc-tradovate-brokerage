using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace QuantConnect.Brokerages.Tradovate.Api
{
    public class TradovateWebSocketClient : IDisposable
    {
        private readonly string _url;
        private readonly string _accessToken;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _receiveTask;
        private bool _isConnected;
        private bool _isAuthenticated;
        private int _requestId = 1;

        public event EventHandler<string> MessageReceived;
        public event EventHandler<Exception> ErrorOccurred;
        public event EventHandler<bool> ConnectionStateChanged;

        public bool IsConnected => _isConnected && _isAuthenticated;

        public TradovateWebSocketClient(string url, string accessToken)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        }

        public void Connect()
        {
            try
            {
                ConnectAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        private async Task ConnectAsync()
        {
            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                await _webSocket.ConnectAsync(new Uri(_url), _cancellationTokenSource.Token);

                _isConnected = true;
                _isAuthenticated = false;

                _receiveTask = Task.Run(ReceiveLoop, _cancellationTokenSource.Token);

                // Wait for 'o' (open frame) then authenticate
                await Task.Delay(500); // Give time for open frame
                await AuthenticateAsync();
            }
            catch (Exception ex)
            {
                _isConnected = false;
                OnConnectionStateChanged(false);
                throw;
            }
        }

        private async Task AuthenticateAsync()
        {
            // Tradovate uses newline-delimited format: operation\nid\nquery\nbody
            var authMessage = $"authorize\n{_requestId++}\n\n{_accessToken}";
            await SendAsync(authMessage);
        }

        public void Disconnect()
        {
            try
            {
                DisconnectAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        private async Task DisconnectAsync()
        {
            try
            {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    _cancellationTokenSource?.Cancel();
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }

                _isConnected = false;
                OnConnectionStateChanged(false);
            }
            catch
            {
                _isConnected = false;
                OnConnectionStateChanged(false);
            }
        }

        public void SubscribeQuote(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
            }

            try
            {
                // Tradovate format: operation\nid\nquery\nbody
                var subscribeMessage = $"md/subscribequote\n{_requestId++}\n\n{symbol}";
                SendAsync(subscribeMessage).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        public void UnsubscribeQuote(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
            }

            try
            {
                // Tradovate format: operation\nid\nquery\nbody
                var unsubscribeMessage = $"md/unsubscribequote\n{_requestId++}\n\n{symbol}";
                SendAsync(unsubscribeMessage).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        public void SendMessage(string message)
        {
            try
            {
                SendAsync(message).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        private async Task SendAsync(string message)
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];

            try
            {
                while (_webSocket.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        _isConnected = false;
                        OnConnectionStateChanged(false);
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    OnMessageReceived(message);
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                OnConnectionStateChanged(false);
                OnError(ex);
            }
        }

        protected virtual void OnMessageReceived(string message)
        {
            // Handle SockJS frame types: o (open), a (array/data), h (heartbeat), c (close)
            if (string.IsNullOrEmpty(message))
                return;

            var frameType = message[0];

            switch (frameType)
            {
                case 'o': // Open frame - connection established
                    // Do nothing, just acknowledgement
                    break;

                case 'h': // Heartbeat frame - keep alive
                    // Do nothing, connection is alive
                    break;

                case 'a': // Array frame - actual data
                    // Strip 'a' prefix and parse JSON array
                    if (message.Length > 1)
                    {
                        var jsonContent = message.Substring(1);
                        ProcessArrayFrame(jsonContent);
                    }
                    break;

                case 'c': // Close frame - connection closing
                    _isConnected = false;
                    _isAuthenticated = false;
                    OnConnectionStateChanged(false);
                    break;

                default:
                    // Unknown frame type, pass through
                    MessageReceived?.Invoke(this, message);
                    break;
            }
        }

        private void ProcessArrayFrame(string jsonContent)
        {
            try
            {
                var messages = JsonConvert.DeserializeObject<JArray>(jsonContent);
                if (messages == null) return;

                foreach (var msg in messages)
                {
                    // Check if this is an authorization response
                    if (msg is JObject obj && obj.ContainsKey("s"))
                    {
                        var status = obj["s"]?.Value<int>();
                        if (status == 200)
                        {
                            _isAuthenticated = true;
                            OnConnectionStateChanged(true);
                        }
                    }

                    // Pass the message to subscribers
                    MessageReceived?.Invoke(this, msg.ToString());
                }
            }
            catch (Exception ex)
            {
                OnError(new Exception($"Failed to parse array frame: {ex.Message}"));
            }
        }

        protected virtual void OnError(Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }

        protected virtual void OnConnectionStateChanged(bool isConnected)
        {
            ConnectionStateChanged?.Invoke(this, isConnected);
        }

        public void Dispose()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _receiveTask?.Wait(TimeSpan.FromSeconds(5));
                _webSocket?.Dispose();
                _cancellationTokenSource?.Dispose();
            }
            catch
            {
            }
        }
    }
}