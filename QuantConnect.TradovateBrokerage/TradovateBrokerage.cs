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
using QuantConnect.Data;
using QuantConnect.Util;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Brokerages.Tradovate.Api;
using QuantConnect.Logging;

namespace QuantConnect.Brokerages.Tradovate
{
    [BrokerageFactory(typeof(TradovateBrokerageFactory))]
    public class TradovateBrokerage : Brokerage, IDataQueueHandler, IDataQueueUniverseProvider
    {
        private readonly IDataAggregator _aggregator;
        private readonly EventBasedDataQueueHandlerSubscriptionManager _subscriptionManager;
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

        /// <summary>
        /// Returns true if we're currently connected to the broker
        /// </summary>
        public override bool IsConnected => _webSocketClient?.IsConnected ?? false;

        /// <summary>
        /// Parameterless constructor for brokerage
        /// </summary>
        /// <remarks>This parameterless constructor is required for brokerages implementing <see cref="IDataQueueHandler"/></remarks>
        public TradovateBrokerage()
            : this(Composer.Instance.GetPart<IDataAggregator>(), string.Empty, string.Empty, string.Empty, string.Empty, TradovateEnvironment.Demo)
        {
        }

        /// <summary>
        /// Creates a new instance with OAuth authentication
        /// </summary>
        /// <param name="aggregator">consolidate ticks</param>
        /// <param name="oauthToken">OAuth access token</param>
        /// <param name="environment">Demo or Live environment</param>
        public TradovateBrokerage(IDataAggregator aggregator, string oauthToken, TradovateEnvironment environment) : base("TradovateBrokerage")
        {
            _aggregator = aggregator;
            _oauthToken = oauthToken;
            _environment = environment;
            _useOAuth = true;
            _symbolMapper = new TradovateSymbolMapper();
            _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
            _subscriptionManager.SubscribeImpl += (s, t) => Subscribe(s);
            _subscriptionManager.UnsubscribeImpl += (s, t) => Unsubscribe(s);
        }

        /// <summary>
        /// Creates a new instance with client credentials authentication
        /// </summary>
        /// <param name="aggregator">consolidate ticks</param>
        /// <param name="username">Tradovate username</param>
        /// <param name="password">Tradovate password</param>
        /// <param name="clientId">Tradovate client ID (appId)</param>
        /// <param name="clientSecret">Tradovate client secret</param>
        /// <param name="environment">Demo or Live environment</param>
        public TradovateBrokerage(IDataAggregator aggregator, string username, string password, string clientId, string clientSecret, TradovateEnvironment environment) : base("TradovateBrokerage")
        {
            _aggregator = aggregator;
            _username = username;
            _password = password;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _environment = environment;
            _useOAuth = false;
            _symbolMapper = new TradovateSymbolMapper();
            _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
            _subscriptionManager.SubscribeImpl += (s, t) => Subscribe(s);
            _subscriptionManager.UnsubscribeImpl += (s, t) => Unsubscribe(s);
        }

        #region IDataQueueHandler

        /// <summary>
        /// Subscribe to the specified configuration
        /// </summary>
        /// <param name="dataConfig">defines the parameters to subscribe to a data feed</param>
        /// <param name="newDataAvailableHandler">handler to be fired on new data available</param>
        /// <returns>The new enumerator for this subscription request</returns>
        public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            if (!CanSubscribe(dataConfig.Symbol))
            {
                return null;
            }

            var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
            _subscriptionManager.Subscribe(dataConfig);

            return enumerator;
        }

        /// <summary>
        /// Removes the specified configuration
        /// </summary>
        /// <param name="dataConfig">Subscription config to be removed</param>
        public void Unsubscribe(SubscriptionDataConfig dataConfig)
        {
            _subscriptionManager.Unsubscribe(dataConfig);
            _aggregator.Remove(dataConfig);
        }

        /// <summary>
        /// Sets the job we're subscribing for
        /// </summary>
        /// <param name="job">Job we're subscribing for</param>
        public void SetJob(LiveNodePacket job)
        {
        }

        #endregion

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
                var orders = _restClient.GetOrderList();
                return new List<Order>();
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "GetOpenOrders", $"Error getting open orders: {ex.Message}"));
                return new List<Order>();
            }
        }

        /// <summary>
        /// Gets all holdings for the account
        /// </summary>
        /// <returns>The current holdings from the account</returns>
        public override List<Holding> GetAccountHoldings()
        {
            if (_restClient == null)
            {
                return new List<Holding>();
            }

            try
            {
                var positions = _restClient.GetPositionList();
                return positions.Select(p => new Holding
                {
                    Symbol = Symbol.Empty,
                    Quantity = p.NetPos,
                    AveragePrice = 0,
                    MarketPrice = 0
                }).ToList();
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "GetAccountHoldings", $"Error getting holdings: {ex.Message}"));
                return new List<Holding>();
            }
        }

        /// <summary>
        /// Gets the current cash balance for each currency held in the brokerage account
        /// </summary>
        /// <returns>The current cash balance for each currency available for trading</returns>
        public override List<CashAmount> GetCashBalance()
        {
            if (_restClient == null)
            {
                return new List<CashAmount>();
            }

            try
            {
                var accounts = _restClient.GetAccountList();
                return accounts.Select(a => new CashAmount(a.Balance, "USD")).ToList();
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
                return false;
            }

            try
            {
                var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(order.Symbol);
                Log.Trace($"TradovateBrokerage.PlaceOrder(): Placing order {order.Id} for {brokerageSymbol}");
                return true;
            }
            catch (Exception ex)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "PlaceOrder", $"Error placing order: {ex.Message}"));
                return false;
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
                var brokerId = int.Parse(order.BrokerId.First());
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

                var wsUrl = _environment == TradovateEnvironment.Demo
                    ? "wss://demo.tradovateapi.com/v1/websocket"
                    : "wss://live.tradovateapi.com/v1/websocket";

                _webSocketClient = new TradovateWebSocketClient(wsUrl, accessToken);
                _webSocketClient.MessageReceived += OnWebSocketMessage;
                _webSocketClient.ErrorOccurred += OnWebSocketError;
                _webSocketClient.Connect();

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

        #region IDataQueueUniverseProvider

        /// <summary>
        /// Method returns a collection of Symbols that are available at the data source.
        /// </summary>
        /// <param name="symbol">Symbol to lookup</param>
        /// <param name="includeExpired">Include expired contracts</param>
        /// <param name="securityCurrency">Expected security currency(if any)</param>
        /// <returns>Enumerable of Symbols, that are associated with the provided Symbol</returns>
        public IEnumerable<Symbol> LookupSymbols(Symbol symbol, bool includeExpired, string securityCurrency = null)
        {
            return Enumerable.Empty<Symbol>();
        }

        /// <summary>
        /// Returns whether selection can take place or not.
        /// </summary>
        /// <remarks>This is useful to avoid a selection taking place during invalid times, for example IB reset times or when not connected,
        /// because if allowed selection would fail since IB isn't running and would kill the algorithm</remarks>
        /// <returns>True if selection can take place</returns>
        public bool CanPerformSelection()
        {
            return IsConnected;
        }

        #endregion

        private bool CanSubscribe(Symbol symbol)
        {
            if (symbol.Value.IndexOfInvariant("universe", true) != -1 || symbol.IsCanonical())
            {
                return false;
            }

            return symbol.SecurityType == SecurityType.Future;
        }

        /// <summary>
        /// Adds the specified symbols to the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be added keyed by SecurityType</param>
        private bool Subscribe(IEnumerable<Symbol> symbols)
        {
            if (_webSocketClient == null || !IsConnected)
            {
                return false;
            }

            foreach (var symbol in symbols)
            {
                try
                {
                    var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);
                    _webSocketClient.SubscribeQuote(brokerageSymbol);
                }
                catch (Exception ex)
                {
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "Subscribe", $"Error subscribing to {symbol}: {ex.Message}"));
                }
            }

            return true;
        }

        /// <summary>
        /// Removes the specified symbols to the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be removed keyed by SecurityType</param>
        private bool Unsubscribe(IEnumerable<Symbol> symbols)
        {
            if (_webSocketClient == null || !IsConnected)
            {
                return false;
            }

            foreach (var symbol in symbols)
            {
                try
                {
                    var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);
                    _webSocketClient.UnsubscribeQuote(brokerageSymbol);
                }
                catch (Exception ex)
                {
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "Unsubscribe", $"Error unsubscribing from {symbol}: {ex.Message}"));
                }
            }

            return true;
        }

        /// <summary>
        /// Gets the history for the requested symbols
        /// <see cref="IBrokerage.GetHistory(Data.HistoryRequest)"/>
        /// </summary>
        /// <param name="request">The historical data request</param>
        /// <returns>An enumerable of bars covering the span specified in the request</returns>
        public override IEnumerable<BaseData> GetHistory(Data.HistoryRequest request)
        {
            if (!CanSubscribe(request.Symbol))
            {
                return null;
            }

            return Enumerable.Empty<BaseData>();
        }

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
    }
}
