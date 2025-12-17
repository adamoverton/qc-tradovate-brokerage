using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantConnect.Logging;

namespace QuantConnect.Brokerages.Tradovate.Api
{
    public class TradovateRestApiClient
    {
        private readonly string _apiUrl;
        private string _accessToken;
        private readonly HttpClient _httpClient;
        private readonly object _tokenLock = new object();

        public TradovateRestApiClient(string apiUrl, string accessToken)
        {
            _apiUrl = apiUrl ?? throw new ArgumentNullException(nameof(apiUrl));
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
        }

        /// <summary>
        /// Updates the access token used for API requests.
        /// Called when the token is refreshed.
        /// </summary>
        /// <param name="newAccessToken">The new access token</param>
        public void UpdateAccessToken(string newAccessToken)
        {
            if (string.IsNullOrEmpty(newAccessToken))
            {
                Log.Error("TradovateRestApiClient.UpdateAccessToken(): Cannot update with null/empty token");
                return;
            }

            lock (_tokenLock)
            {
                _accessToken = newAccessToken;
                // Update the Authorization header
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                Log.Trace("TradovateRestApiClient.UpdateAccessToken(): Token updated successfully");
            }
        }

        public List<TradovateAccount> GetAccountList()
        {
            try
            {
                return GetAccountListAsync().GetAwaiter().GetResult();
            }
            catch
            {
                return new List<TradovateAccount>();
            }
        }

        private async Task<List<TradovateAccount>> GetAccountListAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiUrl}/account/list");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<TradovateAccount>>(content) ?? new List<TradovateAccount>();
                }

                return new List<TradovateAccount>();
            }
            catch
            {
                return new List<TradovateAccount>();
            }
        }

        public List<TradovatePosition> GetPositionList()
        {
            try
            {
                return GetPositionListAsync().GetAwaiter().GetResult();
            }
            catch
            {
                return new List<TradovatePosition>();
            }
        }

        public TradovateCashBalance GetCashBalanceSnapshot(int accountId)
        {
            try
            {
                return GetCashBalanceSnapshotAsync(accountId).GetAwaiter().GetResult();
            }
            catch
            {
                return new TradovateCashBalance { TotalCashValue = 0 };
            }
        }

        private async Task<TradovateCashBalance> GetCashBalanceSnapshotAsync(int accountId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiUrl}/cashBalance/getCashBalanceSnapshot?accountId={accountId}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<TradovateCashBalance>(content) ?? new TradovateCashBalance { TotalCashValue = 0 };
                }

                return new TradovateCashBalance { TotalCashValue = 0 };
            }
            catch
            {
                return new TradovateCashBalance { TotalCashValue = 0 };
            }
        }

        private async Task<List<TradovatePosition>> GetPositionListAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiUrl}/position/list");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<TradovatePosition>>(content) ?? new List<TradovatePosition>();
                }

                return new List<TradovatePosition>();
            }
            catch
            {
                return new List<TradovatePosition>();
            }
        }

        public TradovateContract GetContractById(int contractId)
        {
            try
            {
                return GetContractByIdAsync(contractId).GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }
        }

        private async Task<TradovateContract> GetContractByIdAsync(int contractId)
        {
            try
            {
                if (contractId <= 0)
                {
                    return null;
                }

                var response = await _httpClient.GetAsync($"{_apiUrl}/contract/item?id={contractId}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<TradovateContract>(content);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public long PlaceOrder(TradovateOrder order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            try
            {
                return PlaceOrderAsync(order).GetAwaiter().GetResult();
            }
            catch
            {
                return 0;
            }
        }

        private async Task<long> PlaceOrderAsync(TradovateOrder order)
        {
            try
            {
                var jsonContent = JsonConvert.SerializeObject(order);
                Log.Trace($"TradovateRestApiClient.PlaceOrder(): Request JSON: {jsonContent}");
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiUrl}/order/placeorder", content);
                var responseBody = await response.Content.ReadAsStringAsync();
                Log.Trace($"TradovateRestApiClient.PlaceOrder(): Response: {response.StatusCode} - {responseBody}");

                if (response.IsSuccessStatusCode)
                {
                    // Parse orderId directly from response string to avoid Newtonsoft.Json Int32 overflow during JObject.Parse
                    // The orderId can be very large (e.g., 296445350031) which exceeds Int32.MaxValue
                    var match = System.Text.RegularExpressions.Regex.Match(responseBody, @"""orderId""\s*:\s*(\d+)");
                    if (match.Success && long.TryParse(match.Groups[1].Value, out var orderId))
                    {
                        Log.Trace($"TradovateRestApiClient.PlaceOrder(): Parsed orderId: {orderId}");
                        return orderId;
                    }
                    Log.Error($"TradovateRestApiClient.PlaceOrder(): Failed to parse orderId from response");
                    return 0;
                }

                Log.Error($"TradovateRestApiClient.PlaceOrder(): Non-success status: {response.StatusCode} - {responseBody}");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateRestApiClient.PlaceOrder(): Exception: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Modifies an existing order
        /// </summary>
        /// <param name="request">The modification request containing orderId and fields to update</param>
        /// <returns>True if the modification request was accepted, false otherwise</returns>
        public bool ModifyOrder(ModifyOrderRequest request)
        {
            try
            {
                return ModifyOrderAsync(request).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateRestApiClient.ModifyOrder(): Exception: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ModifyOrderAsync(ModifyOrderRequest request)
        {
            try
            {
                if (request == null || request.OrderId <= 0)
                {
                    Log.Error("TradovateRestApiClient.ModifyOrder(): Invalid request or orderId");
                    return false;
                }

                var jsonContent = JsonConvert.SerializeObject(request);
                Log.Trace($"TradovateRestApiClient.ModifyOrder(): Request JSON: {jsonContent}");
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiUrl}/order/modifyorder", content);
                var responseBody = await response.Content.ReadAsStringAsync();
                Log.Trace($"TradovateRestApiClient.ModifyOrder(): Response: {response.StatusCode} - {responseBody}");

                if (response.IsSuccessStatusCode)
                {
                    // Check if response contains a commandId (indicates success)
                    if (responseBody.Contains("commandId"))
                    {
                        return true;
                    }
                    // Sometimes API returns success but with an error message
                    if (responseBody.Contains("errorText") || responseBody.Contains("failureReason"))
                    {
                        Log.Error($"TradovateRestApiClient.ModifyOrder(): API returned error in response: {responseBody}");
                        return false;
                    }
                    return true;
                }

                Log.Error($"TradovateRestApiClient.ModifyOrder(): Non-success status: {response.StatusCode} - {responseBody}");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateRestApiClient.ModifyOrder(): Exception: {ex.Message}");
                return false;
            }
        }

        public bool CancelOrder(long orderId)
        {
            try
            {
                return CancelOrderAsync(orderId).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CancelOrderAsync(long orderId)
        {
            try
            {
                if (orderId <= 0)
                {
                    return false;
                }

                var payload = new { orderId };
                var jsonContent = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiUrl}/order/cancelorder", content);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public List<TradovateOrder> GetOrderList()
        {
            try
            {
                return GetOrderListAsync().GetAwaiter().GetResult();
            }
            catch
            {
                return new List<TradovateOrder>();
            }
        }

        private async Task<List<TradovateOrder>> GetOrderListAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiUrl}/order/list");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<TradovateOrder>>(content) ?? new List<TradovateOrder>();
                }

                return new List<TradovateOrder>();
            }
            catch
            {
                return new List<TradovateOrder>();
            }
        }

        public List<TradovateContract> SuggestContract(string searchText)
        {
            try
            {
                return SuggestContractAsync(searchText).GetAwaiter().GetResult();
            }
            catch
            {
                return new List<TradovateContract>();
            }
        }

        private async Task<List<TradovateContract>> SuggestContractAsync(string searchText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    return new List<TradovateContract>();
                }

                var response = await _httpClient.GetAsync($"{_apiUrl}/contract/suggest?t={Uri.EscapeDataString(searchText)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<TradovateContract>>(content) ?? new List<TradovateContract>();
                }

                return new List<TradovateContract>();
            }
            catch
            {
                return new List<TradovateContract>();
            }
        }

        /// <summary>
        /// Starts a bracket order strategy (entry with stop loss and take profit)
        /// </summary>
        /// <param name="request">The bracket order request</param>
        /// <returns>The order strategy ID if successful, 0 otherwise</returns>
        public long StartOrderStrategy(StartOrderStrategyRequest request)
        {
            try
            {
                return StartOrderStrategyAsync(request).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateRestApiClient.StartOrderStrategy(): Exception: {ex.Message}");
                return 0;
            }
        }

        private async Task<long> StartOrderStrategyAsync(StartOrderStrategyRequest request)
        {
            try
            {
                var jsonContent = JsonConvert.SerializeObject(request);
                Log.Trace($"TradovateRestApiClient.StartOrderStrategy(): Request JSON: {jsonContent}");
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiUrl}/orderStrategy/startOrderStrategy", content);
                var responseBody = await response.Content.ReadAsStringAsync();
                Log.Trace($"TradovateRestApiClient.StartOrderStrategy(): Response: {response.StatusCode} - {responseBody}");

                if (response.IsSuccessStatusCode)
                {
                    // Parse orderStrategyId from response
                    var match = System.Text.RegularExpressions.Regex.Match(responseBody, @"""orderStrategyId""\s*:\s*(\d+)");
                    if (match.Success && long.TryParse(match.Groups[1].Value, out var strategyId))
                    {
                        Log.Trace($"TradovateRestApiClient.StartOrderStrategy(): Parsed orderStrategyId: {strategyId}");
                        return strategyId;
                    }
                    // Also check for just "id" field
                    match = System.Text.RegularExpressions.Regex.Match(responseBody, @"""id""\s*:\s*(\d+)");
                    if (match.Success && long.TryParse(match.Groups[1].Value, out strategyId))
                    {
                        Log.Trace($"TradovateRestApiClient.StartOrderStrategy(): Parsed id: {strategyId}");
                        return strategyId;
                    }
                    Log.Error($"TradovateRestApiClient.StartOrderStrategy(): Failed to parse orderStrategyId from response");
                    return 0;
                }

                Log.Error($"TradovateRestApiClient.StartOrderStrategy(): Non-success status: {response.StatusCode} - {responseBody}");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateRestApiClient.StartOrderStrategy(): Exception: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Cancels an order strategy
        /// </summary>
        /// <param name="orderStrategyId">The order strategy ID to cancel</param>
        /// <returns>True if cancellation request succeeded</returns>
        public bool CancelOrderStrategy(long orderStrategyId)
        {
            try
            {
                return CancelOrderStrategyAsync(orderStrategyId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateRestApiClient.CancelOrderStrategy(): Exception: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CancelOrderStrategyAsync(long orderStrategyId)
        {
            try
            {
                if (orderStrategyId <= 0)
                {
                    return false;
                }

                var payload = new { orderStrategyId };
                var jsonContent = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiUrl}/orderStrategy/cancelOrderStrategy", content);
                var responseBody = await response.Content.ReadAsStringAsync();
                Log.Trace($"TradovateRestApiClient.CancelOrderStrategy(): Response: {response.StatusCode} - {responseBody}");

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateRestApiClient.CancelOrderStrategy(): Exception: {ex.Message}");
                return false;
            }
        }
    }

    public class TradovateAccount
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int UserId { get; set; }
        public bool Active { get; set; }
        public bool Restricted { get; set; }
        public bool Closed { get; set; }
        public bool Archived { get; set; }
    }

    public class TradovateCashBalance
    {
        [JsonProperty("totalCashValue")]
        public decimal TotalCashValue { get; set; }
        [JsonProperty("netLiq")]
        public decimal NetLiq { get; set; }
        [JsonProperty("realizedPnL")]
        public decimal RealizedPnL { get; set; }
        [JsonProperty("openPnL")]
        public decimal OpenPnL { get; set; }
    }

    public class TradovatePosition
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public int ContractId { get; set; }
        public int NetPos { get; set; }
    }

    public class TradovateContract
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
    }

    public class TradovateOrder
    {
        // Required fields for placing orders
        [JsonProperty("accountId")]
        public int AccountId { get; set; }

        [JsonProperty("contractId")]
        public int ContractId { get; set; }

        [JsonProperty("symbol")]
        public string Symbol { get; set; }  // Required by Tradovate API (e.g., "YMZ5")

        [JsonProperty("action")]
        public string Action { get; set; }  // "Buy" or "Sell"

        [JsonProperty("orderQty")]
        public int Quantity { get; set; }

        [JsonProperty("orderType")]
        public string OrderType { get; set; }  // "Market", "Limit", "Stop", "StopLimit"

        // Price fields (used for limit/stop orders)
        [JsonProperty("price")]
        public decimal? Price { get; set; }  // Limit price

        [JsonProperty("stopPrice")]
        public decimal? StopPrice { get; set; }  // Stop trigger price

        // Required for automated trading
        [JsonProperty("isAutomated")]
        public bool IsAutomated { get; set; } = true;

        // Response fields (populated when reading orders from API)
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("ordStatus")]
        public string OrdStatus { get; set; }  // "Working", "Filled", "Cancelled", "Rejected", etc.

        [JsonProperty("filledQty")]
        public int FilledQty { get; set; }

        [JsonProperty("avgFillPrice")]
        public decimal? AvgFillPrice { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }  // Error/info message from broker

        [JsonProperty("timeInForce")]
        public string TimeInForce { get; set; }  // "Day", "GTC", etc.
    }

    /// <summary>
    /// Request object for modifying an existing order via /order/modifyorder
    /// </summary>
    public class ModifyOrderRequest
    {
        /// <summary>
        /// The ID of the order to modify (required)
        /// </summary>
        [JsonProperty("orderId")]
        public long OrderId { get; set; }

        /// <summary>
        /// New quantity for the order (required)
        /// </summary>
        [JsonProperty("orderQty")]
        public int OrderQty { get; set; }

        /// <summary>
        /// Order type - must match existing order or be valid change (required)
        /// </summary>
        [JsonProperty("orderType")]
        public string OrderType { get; set; }

        /// <summary>
        /// New limit price (for Limit and StopLimit orders)
        /// </summary>
        [JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? Price { get; set; }

        /// <summary>
        /// New stop price (for Stop and StopLimit orders)
        /// </summary>
        [JsonProperty("stopPrice", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? StopPrice { get; set; }

        /// <summary>
        /// Time in force - CRITICAL: Must match the existing order's TIF setting
        /// </summary>
        [JsonProperty("timeInForce")]
        public string TimeInForce { get; set; } = "GTC";

        /// <summary>
        /// Whether this is an automated order
        /// </summary>
        [JsonProperty("isAutomated")]
        public bool IsAutomated { get; set; } = true;
    }

    /// <summary>
    /// Request object for starting a bracket order strategy via /orderStrategy/startOrderStrategy
    /// </summary>
    public class StartOrderStrategyRequest
    {
        /// <summary>
        /// Account ID (required, must be integer)
        /// </summary>
        [JsonProperty("accountId")]
        public int AccountId { get; set; }

        /// <summary>
        /// Account name/spec (required)
        /// </summary>
        [JsonProperty("accountSpec")]
        public string AccountSpec { get; set; }

        /// <summary>
        /// Contract symbol (e.g., "YMZ5")
        /// </summary>
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        /// <summary>
        /// Order direction: "Buy" or "Sell"
        /// </summary>
        [JsonProperty("action")]
        public string Action { get; set; }

        /// <summary>
        /// Order strategy type ID. Use 2 for bracket orders (only supported type)
        /// </summary>
        [JsonProperty("orderStrategyTypeId")]
        public int OrderStrategyTypeId { get; set; } = 2;

        /// <summary>
        /// Strategy parameters as a JSON string (NOT an object - must be stringified)
        /// </summary>
        [JsonProperty("params")]
        public string Params { get; set; }
    }

    /// <summary>
    /// Inner parameters for bracket order strategy (serialized to JSON string for Params field)
    /// </summary>
    public class BracketOrderParams
    {
        /// <summary>
        /// Entry order configuration
        /// </summary>
        [JsonProperty("entryVersion")]
        public BracketEntryVersion EntryVersion { get; set; }

        /// <summary>
        /// Bracket exit orders (stop loss and take profit)
        /// </summary>
        [JsonProperty("brackets")]
        public List<BracketExit> Brackets { get; set; }
    }

    /// <summary>
    /// Entry order configuration for bracket order
    /// </summary>
    public class BracketEntryVersion
    {
        /// <summary>
        /// Entry order quantity
        /// </summary>
        [JsonProperty("orderQty")]
        public int OrderQty { get; set; }

        /// <summary>
        /// Entry order type: "Market", "Limit", "Stop", "StopLimit"
        /// </summary>
        [JsonProperty("orderType")]
        public string OrderType { get; set; }

        /// <summary>
        /// Time in force: "GTC" or "Day"
        /// </summary>
        [JsonProperty("timeInForce")]
        public string TimeInForce { get; set; } = "GTC";

        /// <summary>
        /// Set to 0 for new orders
        /// </summary>
        [JsonProperty("orderId")]
        public int OrderId { get; set; } = 0;

        /// <summary>
        /// Limit price (for Limit/StopLimit orders)
        /// </summary>
        [JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? Price { get; set; }

        /// <summary>
        /// Stop price (for Stop/StopLimit orders)
        /// </summary>
        [JsonProperty("stopPrice", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? StopPrice { get; set; }
    }

    /// <summary>
    /// Bracket exit order configuration (stop loss and take profit)
    /// </summary>
    public class BracketExit
    {
        /// <summary>
        /// Exit quantity
        /// </summary>
        [JsonProperty("qty")]
        public int Qty { get; set; }

        /// <summary>
        /// Take profit target in points/ticks from entry
        /// </summary>
        [JsonProperty("profitTarget")]
        public decimal ProfitTarget { get; set; }

        /// <summary>
        /// Stop loss in points/ticks from entry (use negative value, e.g., -5 for 5 point stop)
        /// </summary>
        [JsonProperty("stopLoss")]
        public decimal StopLoss { get; set; }

        /// <summary>
        /// Whether to use trailing stop (default false for fixed stops)
        /// </summary>
        [JsonProperty("trailingStop")]
        public bool TrailingStop { get; set; } = false;
    }
}