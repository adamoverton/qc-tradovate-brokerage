/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Brokerages.Tradovate.Api;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Securities;

namespace QuantConnect.Brokerages.Tradovate
{
    /// <summary>
    /// Tradovate brokerage implementation for QuantConnect LEAN.
    /// This is an execution-only brokerage - it handles order placement, cancellation,
    /// and position management but does NOT provide market data feeds.
    ///
    /// For live trading, use a separate data source (IQFeed, Databento, QC Cloud, etc.)
    /// alongside this brokerage for execution.
    ///
    /// Reason: CME requires a sub-vendor license (~$290-375/month per exchange) for API
    /// market data access. Since Tradovate is popular with budget-conscious prop firm traders,
    /// this cost is prohibitive for most users. See reference/market-data.md for details.
    /// </summary>
    [BrokerageFactory(typeof(TradovateBrokerageFactory))]
    public class TradovateBrokerage : Brokerage
    {
        private readonly TradovateSymbolMapper _symbolMapper;
        private TradovateAuthManager _authManager;
        private TradovateRestApiClient _restClient;
        private TradovateWebSocketClient _webSocketClient;
        private readonly string _username;
        private readonly string _password;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _oauthToken;
        private readonly TradovateEnvironment _environment;
        private readonly bool _useOAuth;
        private int? _cachedAccountId;
        private readonly Dictionary<string, int> _contractIdCache = new Dictionary<string, int>();

        /// <summary>
        /// Returns true if we're currently connected to the broker
        /// For execution-only mode, we just need the REST client to be initialized
        /// </summary>
        public override bool IsConnected
        {
            get
            {
                // For execution-only brokerage, REST client is sufficient
                var connected = _restClient != null && _authManager?.IsAuthenticated == true;
                Log.Trace($"TradovateBrokerage.IsConnected: {connected}");
                return connected;
            }
        }

        /// <summary>
        /// Gets the account base currency
        /// </summary>
        public override string AccountBaseCurrency
        {
            get
            {
                Log.Trace("TradovateBrokerage.AccountBaseCurrency: returning USD");
                return "USD";
            }
        }

        /// <summary>
        /// Parameterless constructor for brokerage
        /// </summary>
        public TradovateBrokerage()
            : this(string.Empty, string.Empty, string.Empty, string.Empty, TradovateEnvironment.Demo)
        {
        }

        /// <summary>
        /// Creates a new instance with OAuth authentication
        /// </summary>
        /// <param name="oauthToken">OAuth access token</param>
        /// <param name="environment">Demo or Live environment</param>
        public TradovateBrokerage(string oauthToken, TradovateEnvironment environment) : base("TradovateBrokerage")
        {
            _oauthToken = oauthToken;
            _environment = environment;
            _useOAuth = true;
            _symbolMapper = new TradovateSymbolMapper();
        }

        /// <summary>
        /// Creates a new instance with client credentials authentication
        /// </summary>
        /// <param name="username">Tradovate username</param>
        /// <param name="password">Tradovate password</param>
        /// <param name="clientId">Tradovate client ID (appId)</param>
        /// <param name="clientSecret">Tradovate client secret</param>
        /// <param name="environment">Demo or Live environment</param>
        // Increment this when making code changes to verify correct DLL is loaded
        private const string BrokerageVersion = "2024-12-14-v3-regex-parsing";

        public TradovateBrokerage(string username, string password, string clientId, string clientSecret, TradovateEnvironment environment) : base("TradovateBrokerage")
        {
            // Log version info at construction to verify correct DLL is loaded
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var assemblyDate = System.IO.File.GetLastWriteTime(assembly.Location);
            Log.Trace($"TradovateBrokerage: Version={BrokerageVersion}, Assembly={assembly.Location}, Modified={assemblyDate:yyyy-MM-dd HH:mm:ss}");

            _username = username;
            _password = password;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _environment = environment;
            _useOAuth = false;
            _symbolMapper = new TradovateSymbolMapper();
        }

        #region Brokerage

        /// <summary>
        /// Gets all open orders on the account.
        /// NOTE: The order objects returned do not have QC order IDs.
        /// </summary>
        /// <returns>The open orders returned from Tradovate</returns>
        public override List<Order> GetOpenOrders()
        {
            if (_restClient == null)
            {
                return new List<Order>();
            }

            try
            {
                var tradovateOrders = _restClient.GetOrderList();
                var qcOrders = new List<Order>();

                foreach (var tvOrder in tradovateOrders)
                {
                    // Only include working/pending orders
                    if (!IsOpenOrderStatus(tvOrder.OrdStatus))
                    {
                        continue;
                    }

                    // Look up contract to get symbol
                    var contract = _restClient.GetContractById(tvOrder.ContractId);
                    if (contract == null)
                    {
                        Log.Trace($"TradovateBrokerage.GetOpenOrders(): WARNING - Could not find contract {tvOrder.ContractId}");
                        continue;
                    }

                    // Convert brokerage symbol to QC symbol
                    Symbol qcSymbol;
                    try
                    {
                        qcSymbol = _symbolMapper.GetLeanSymbol(contract.Name, SecurityType.Future, Market.CME);
                    }
                    catch (Exception ex)
                    {
                        Log.Trace($"TradovateBrokerage.GetOpenOrders(): WARNING - Could not map symbol {contract.Name}: {ex.Message}");
                        continue;
                    }

                    // Determine quantity (positive for buy, negative for sell)
                    var quantity = tvOrder.Action == "Sell" ? -tvOrder.Quantity : tvOrder.Quantity;

                    // Create appropriate QC order type
                    Order qcOrder = CreateQcOrder(tvOrder, qcSymbol, quantity);
                    if (qcOrder == null)
                    {
                        continue;
                    }

                    // Set broker ID
                    qcOrder.BrokerId.Add(tvOrder.Id.ToString());

                    qcOrders.Add(qcOrder);
                }

                Log.Trace($"TradovateBrokerage.GetOpenOrders(): Found {qcOrders.Count} open orders");
                return qcOrders;
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "GetOpenOrders", $"Error getting open orders: {ex.Message}"));
                return new List<Order>();
            }
        }

        /// <summary>
        /// Determines if an order status represents an open/working order
        /// </summary>
        private static bool IsOpenOrderStatus(string ordStatus)
        {
            if (string.IsNullOrEmpty(ordStatus))
            {
                return false;
            }

            // Tradovate order statuses that indicate an open order
            var openStatuses = new[] { "Working", "Accepted", "PendingNew", "PendingCancel", "PendingReplace" };
            return openStatuses.Contains(ordStatus, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a QC Order from a Tradovate order
        /// </summary>
        private Order CreateQcOrder(TradovateOrder tvOrder, Symbol symbol, decimal quantity)
        {
            switch (tvOrder.OrderType?.ToLowerInvariant())
            {
                case "market":
                    return new MarketOrder(symbol, quantity, DateTime.UtcNow);

                case "limit":
                    if (!tvOrder.Price.HasValue)
                    {
                        Log.Trace($"TradovateBrokerage.CreateQcOrder(): WARNING - Limit order {tvOrder.Id} missing price");
                        return null;
                    }
                    return new LimitOrder(symbol, quantity, tvOrder.Price.Value, DateTime.UtcNow);

                case "stop":
                    if (!tvOrder.StopPrice.HasValue)
                    {
                        Log.Trace($"TradovateBrokerage.CreateQcOrder(): WARNING - Stop order {tvOrder.Id} missing stop price");
                        return null;
                    }
                    return new StopMarketOrder(symbol, quantity, tvOrder.StopPrice.Value, DateTime.UtcNow);

                case "stoplimit":
                    if (!tvOrder.Price.HasValue || !tvOrder.StopPrice.HasValue)
                    {
                        Log.Trace($"TradovateBrokerage.CreateQcOrder(): WARNING - StopLimit order {tvOrder.Id} missing price fields");
                        return null;
                    }
                    return new StopLimitOrder(symbol, quantity, tvOrder.StopPrice.Value, tvOrder.Price.Value, DateTime.UtcNow);

                default:
                    Log.Trace($"TradovateBrokerage.CreateQcOrder(): WARNING - Unknown order type {tvOrder.OrderType}");
                    return null;
            }
        }

        /// <summary>
        /// Gets all holdings for the account
        /// </summary>
        /// <returns>The current holdings from the account</returns>
        public override List<Holding> GetAccountHoldings()
        {
            // For now, return empty list since we don't have proper symbol mapping yet
            // TODO: Map Tradovate contract IDs to QuantConnect symbols
            return new List<Holding>();
        }

        /// <summary>
        /// Gets the current cash balance for each currency held in the brokerage account
        /// </summary>
        /// <returns>The current cash balance for each currency available for trading</returns>
        public override List<CashAmount> GetCashBalance()
        {
            if (_restClient == null)
            {
                return new List<CashAmount> { new CashAmount(0, "USD") };
            }

            try
            {
                var accounts = _restClient.GetAccountList();
                if (accounts == null || accounts.Count == 0)
                {
                    return new List<CashAmount> { new CashAmount(0, "USD") };
                }

                var accountId = accounts[0].Id;
                var cashBalance = _restClient.GetCashBalanceSnapshot(accountId);
                return new List<CashAmount> { new CashAmount(cashBalance.TotalCashValue, "USD") };
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "GetCashBalance", $"Error getting cash balance: {ex.Message}"));
                return new List<CashAmount> { new CashAmount(0, "USD") };
            }
        }

        /// <summary>
        /// Places a new order and assigns a new broker ID to the order
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <returns>True if the request for a new order has been placed, false otherwise</returns>
        public override bool PlaceOrder(Order order)
        {
            if (_restClient == null)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "PlaceOrder", "REST client not initialized"));
                return false;
            }

            try
            {
                // Get account ID (cached after first call)
                var accountId = GetAccountId();
                if (accountId <= 0)
                {
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "PlaceOrder", "Failed to get account ID"));
                    return false;
                }

                // Get brokerage symbol and contract ID
                var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(order.Symbol);
                var contractId = GetContractId(brokerageSymbol);
                if (contractId <= 0)
                {
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "PlaceOrder", $"Failed to get contract ID for {brokerageSymbol}"));
                    return false;
                }

                // Build Tradovate order
                var tradovateOrder = new TradovateOrder
                {
                    AccountId = accountId,
                    ContractId = contractId,
                    Symbol = brokerageSymbol,  // Required by Tradovate API
                    Action = order.Quantity > 0 ? "Buy" : "Sell",
                    Quantity = Math.Abs((int)order.Quantity),
                    OrderType = MapOrderType(order),
                    IsAutomated = true
                };

                // Set price fields based on order type
                switch (order)
                {
                    case LimitOrder limitOrder:
                        tradovateOrder.Price = limitOrder.LimitPrice;
                        break;
                    case StopMarketOrder stopOrder:
                        tradovateOrder.StopPrice = stopOrder.StopPrice;
                        break;
                    case StopLimitOrder stopLimitOrder:
                        tradovateOrder.Price = stopLimitOrder.LimitPrice;
                        tradovateOrder.StopPrice = stopLimitOrder.StopPrice;
                        break;
                }

                Log.Trace($"TradovateBrokerage.PlaceOrder(): Placing {tradovateOrder.OrderType} order {order.Id} for {brokerageSymbol}: {tradovateOrder.Action} {tradovateOrder.Quantity} @ {tradovateOrder.Price ?? tradovateOrder.StopPrice ?? 0m}");

                // Place order via REST API
                var tradovateOrderId = _restClient.PlaceOrder(tradovateOrder);
                if (tradovateOrderId <= 0)
                {
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "PlaceOrder", $"Failed to place order for {brokerageSymbol}"));
                    return false;
                }

                // Set broker ID on the order
                order.BrokerId.Add(tradovateOrderId.ToString());

                Log.Trace($"TradovateBrokerage.PlaceOrder(): Order {order.Id} placed successfully, broker ID: {tradovateOrderId}");

                // Fire order event indicating order was submitted
                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, "Order submitted to Tradovate")
                {
                    Status = OrderStatus.Submitted
                });

                return true;
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "PlaceOrder", $"Error placing order: {ex.Message}"));
                return false;
            }
        }

        /// <summary>
        /// Gets the account ID, caching after first retrieval
        /// </summary>
        private int GetAccountId()
        {
            if (_cachedAccountId.HasValue)
            {
                return _cachedAccountId.Value;
            }

            var accounts = _restClient.GetAccountList();
            if (accounts == null || accounts.Count == 0)
            {
                Log.Error("TradovateBrokerage.GetAccountId(): No accounts found");
                return 0;
            }

            _cachedAccountId = accounts[0].Id;
            Log.Trace($"TradovateBrokerage.GetAccountId(): Using account ID {_cachedAccountId.Value} ({accounts[0].Name})");
            return _cachedAccountId.Value;
        }

        /// <summary>
        /// Gets the contract ID for a symbol, caching results
        /// </summary>
        private int GetContractId(string brokerageSymbol)
        {
            if (_contractIdCache.TryGetValue(brokerageSymbol, out var cachedId))
            {
                return cachedId;
            }

            var contracts = _restClient.SuggestContract(brokerageSymbol);
            if (contracts == null || contracts.Count == 0)
            {
                Log.Error($"TradovateBrokerage.GetContractId(): No contracts found for {brokerageSymbol}");
                return 0;
            }

            // Find exact match or first result
            var contract = contracts.Find(c => c.Name == brokerageSymbol || c.Symbol == brokerageSymbol) ?? contracts[0];
            _contractIdCache[brokerageSymbol] = contract.Id;
            Log.Trace($"TradovateBrokerage.GetContractId(): Mapped {brokerageSymbol} to contract ID {contract.Id} ({contract.Name})");
            return contract.Id;
        }

        /// <summary>
        /// Maps a QuantConnect order to a Tradovate order type string
        /// </summary>
        private static string MapOrderType(Order order)
        {
            switch (order)
            {
                case MarketOrder _:
                    return "Market";
                case LimitOrder _:
                    return "Limit";
                case StopMarketOrder _:
                    return "Stop";
                case StopLimitOrder _:
                    return "StopLimit";
                default:
                    throw new NotSupportedException($"Order type {order.GetType().Name} is not supported by Tradovate");
            }
        }

        /// <summary>
        /// Updates the order with the same id
        /// </summary>
        /// <param name="order">The new order information</param>
        /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
        public override bool UpdateOrder(Order order)
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UpdateOrder", "Order updates not supported by Tradovate. Cancel and replace instead."));
            return false;
        }

        /// <summary>
        /// Cancels the order with the specified ID
        /// </summary>
        /// <param name="order">The order to cancel</param>
        /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
        public override bool CancelOrder(Order order)
        {
            if (_restClient == null || string.IsNullOrEmpty(order.BrokerId.FirstOrDefault()))
            {
                return false;
            }

            try
            {
                var brokerId = long.Parse(order.BrokerId.First());
                return _restClient.CancelOrder(brokerId);
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "CancelOrder", $"Error canceling order: {ex.Message}"));
                return false;
            }
        }

        /// <summary>
        /// Connects the client to the broker's remote servers
        /// </summary>
        public override void Connect()
        {
            try
            {
                if (_useOAuth)
                {
                    _authManager = new TradovateAuthManager(_oauthToken, _environment);
                }
                else
                {
                    _authManager = new TradovateAuthManager(_username, _password, _clientId, _clientSecret, _environment);
                }

                if (!_authManager.Authenticate())
                {
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "Authentication", "Failed to authenticate with Tradovate"));
                    return;
                }

                var accessToken = _authManager.GetAccessToken();
                var apiUrl = _authManager.GetApiUrl();

                _restClient = new TradovateRestApiClient(apiUrl, accessToken);

                // WebSocket is optional for execution-only mode
                // We can add it later if we need real-time order updates
                try
                {
                    var wsUrl = _environment == TradovateEnvironment.Demo
                        ? "wss://demo.tradovateapi.com/v1/websocket"
                        : "wss://live.tradovateapi.com/v1/websocket";

                    _webSocketClient = new TradovateWebSocketClient(wsUrl, accessToken);
                    _webSocketClient.MessageReceived += OnWebSocketMessage;
                    _webSocketClient.ErrorOccurred += OnWebSocketError;
                    _webSocketClient.Connect();
                }
                catch (Exception wsEx)
                {
                    // WebSocket is optional, log but continue
                    Log.Trace($"TradovateBrokerage: WebSocket connection failed (optional): {wsEx.Message}");
                }

                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Information, "Connection", "Successfully connected to Tradovate"));
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "Connection", $"Failed to connect: {ex.Message}"));
            }
        }

        /// <summary>
        /// Disconnects the client from the broker's remote servers
        /// </summary>
        public override void Disconnect()
        {
            try
            {
                _webSocketClient?.Disconnect();
                _webSocketClient?.Dispose();
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Information, "Disconnect", "Disconnected from Tradovate"));
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "Disconnect", $"Error during disconnect: {ex.Message}"));
            }
        }

        #endregion

        #region Private Helpers

        private void OnWebSocketMessage(object sender, string message)
        {
            try
            {
                Log.Trace($"TradovateBrokerage.OnWebSocketMessage(): {message}");
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "WebSocket", $"Error processing message: {ex.Message}"));
            }
        }

        private void OnWebSocketError(object sender, Exception ex)
        {
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, "WebSocket", $"WebSocket error: {ex.Message}"));
        }

        #endregion
    }
}
