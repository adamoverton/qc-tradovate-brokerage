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
    /// <summary>
    /// Event data for Tradovate order updates received via WebSocket
    /// </summary>
    public class TradovateOrderUpdate
    {
        public string EventType { get; set; }      // Created, Updated, Deleted
        public string EntityType { get; set; }     // order, executionReport, fill
        public long OrderId { get; set; }
        public int AccountId { get; set; }
        public int ContractId { get; set; }
        public string OrdStatus { get; set; }      // Working, Filled, Cancelled, Rejected
        public string Action { get; set; }         // Buy, Sell
        public string OrderType { get; set; }      // Market, Limit, Stop, StopLimit
        public int Qty { get; set; }
        public int FilledQty { get; set; }
        public decimal? Price { get; set; }
        public decimal? StopPrice { get; set; }
        public decimal? AvgFillPrice { get; set; }
        public string Text { get; set; }           // Error/info message
    }

    public class TradovateWebSocketClient : IDisposable
    {
        private readonly string _url;
        private string _accessToken;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _receiveTask;
        private Task _heartbeatTask;
        private bool _isConnected;
        private bool _isAuthenticated;
        private int _requestId = 1;
        private TaskCompletionSource<bool> _authenticationCompletionSource;
        private readonly object _tokenLock = new object();

        // Heartbeat interval - Tradovate requires heartbeat every ~2.5 seconds
        private const int HeartbeatIntervalMs = 2000;

        public event EventHandler<string> MessageReceived;
        public event EventHandler<Exception> ErrorOccurred;
        public event EventHandler<bool> ConnectionStateChanged;
        public event EventHandler<TradovateOrderUpdate> OrderUpdateReceived;

        public bool IsConnected => _isConnected && _isAuthenticated;

        public TradovateWebSocketClient(string url, string accessToken)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        }

        /// <summary>
        /// Updates the access token and re-authenticates the WebSocket connection.
        /// Called when the token is refreshed by TradovateAuthManager.
        /// </summary>
        /// <param name="newAccessToken">The new access token</param>
        /// <returns>True if re-authentication succeeded, false otherwise</returns>
        public bool UpdateAccessToken(string newAccessToken)
        {
            if (string.IsNullOrEmpty(newAccessToken))
            {
                return false;
            }

            lock (_tokenLock)
            {
                _accessToken = newAccessToken;
            }

            // Re-authenticate with the new token if connected
            if (_isConnected && _webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    ReauthenticateAsync(newAccessToken).GetAwaiter().GetResult();
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return true; // Token updated, will be used on next connection
        }

        private async Task ReauthenticateAsync(string token)
        {
            // Send a new authorize message with the refreshed token
            var authMessage = $"authorize\n{_requestId++}\n\n{token}";
            await SendAsync(authMessage);
            // Note: We don't wait for response here - the existing message handler will process it
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
                _authenticationCompletionSource = new TaskCompletionSource<bool>();

                await _webSocket.ConnectAsync(new Uri(_url), _cancellationTokenSource.Token);

                _isConnected = true;
                _isAuthenticated = false;

                _receiveTask = Task.Run(ReceiveLoop, _cancellationTokenSource.Token);
                _heartbeatTask = Task.Run(HeartbeatLoop, _cancellationTokenSource.Token);

                // Wait for 'o' (open frame) then authenticate
                await Task.Delay(500); // Give time for open frame
                await AuthenticateAsync();

                // Wait for authentication to complete (with 10 second timeout)
                var authTask = _authenticationCompletionSource.Task;
                var timeoutTask = Task.Delay(10000);
                var completedTask = await Task.WhenAny(authTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException("Authentication timeout after 10 seconds");
                }

                if (!authTask.Result)
                {
                    throw new Exception("Authentication failed");
                }
            }
            catch (Exception)
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

        /// <summary>
        /// Subscribe to user data events (orders, positions, fills, cash balance changes)
        /// </summary>
        /// <param name="userId">The Tradovate user ID to subscribe to</param>
        public void SubscribeUserSync(int userId)
        {
            try
            {
                // Tradovate format: operation\nid\nquery\nbody
                // Body is JSON with users array
                var body = JsonConvert.SerializeObject(new { users = new[] { userId } });
                var subscribeMessage = $"user/syncrequest\n{_requestId++}\n\n{body}";
                SendAsync(subscribeMessage).GetAwaiter().GetResult();
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
            // Buffer for individual receive operations
            var buffer = new byte[65536];
            // StringBuilder to accumulate fragmented messages
            var messageBuilder = new StringBuilder();

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

                    // Accumulate the message fragment
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    // Only process the message when we have the complete message
                    if (result.EndOfMessage)
                    {
                        var message = messageBuilder.ToString();
                        messageBuilder.Clear();
                        OnMessageReceived(message);
                    }
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                OnConnectionStateChanged(false);
                OnError(ex);
            }
        }

        private async Task HeartbeatLoop()
        {
            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(HeartbeatIntervalMs, _cancellationTokenSource.Token);

                    if (_webSocket?.State == WebSocketState.Open)
                    {
                        // Send empty JSON array as heartbeat per Tradovate documentation
                        await SendAsync("[]");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception)
            {
                // Heartbeat failure - connection will likely be detected as closed elsewhere
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

                            // Signal that authentication is complete
                            _authenticationCompletionSource?.TrySetResult(true);
                        }
                        else
                        {
                            // Authentication failed with non-200 status
                            _isAuthenticated = false;
                            _authenticationCompletionSource?.TrySetResult(false);
                        }
                    }

                    // Check for user/syncrequest events (order updates, fills, etc.)
                    if (msg is JObject eventObj && eventObj.ContainsKey("e") && eventObj["e"]?.ToString() == "props")
                    {
                        ProcessPropsEvent(eventObj);
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

        private void ProcessPropsEvent(JObject eventObj)
        {
            try
            {
                var data = eventObj["d"] as JObject;
                if (data == null) return;

                var entityType = data["entityType"]?.ToString();
                var eventType = data["eventType"]?.ToString();
                var entity = data["entity"] as JObject;

                if (entity == null) return;

                // Process order-related events
                if (entityType == "order" || entityType == "executionReport")
                {
                    var orderUpdate = new TradovateOrderUpdate
                    {
                        EventType = eventType,
                        EntityType = entityType,
                        OrderId = entity["id"]?.Value<long>() ?? 0,
                        AccountId = entity["accountId"]?.Value<int>() ?? 0,
                        ContractId = entity["contractId"]?.Value<int>() ?? 0,
                        OrdStatus = entity["ordStatus"]?.ToString(),
                        Action = entity["action"]?.ToString(),
                        OrderType = entity["orderType"]?.ToString(),
                        Qty = entity["qty"]?.Value<int>() ?? entity["orderQty"]?.Value<int>() ?? 0,
                        FilledQty = entity["filledQty"]?.Value<int>() ?? 0,
                        Price = entity["price"]?.Value<decimal?>(),
                        StopPrice = entity["stopPrice"]?.Value<decimal?>(),
                        AvgFillPrice = entity["avgFillPrice"]?.Value<decimal?>(),
                        Text = entity["text"]?.ToString()
                    };

                    OrderUpdateReceived?.Invoke(this, orderUpdate);
                }
            }
            catch (Exception ex)
            {
                OnError(new Exception($"Failed to process props event: {ex.Message}"));
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
                _heartbeatTask?.Wait(TimeSpan.FromSeconds(2));
                _webSocket?.Dispose();
                _cancellationTokenSource?.Dispose();
            }
            catch
            {
            }
        }
    }
}