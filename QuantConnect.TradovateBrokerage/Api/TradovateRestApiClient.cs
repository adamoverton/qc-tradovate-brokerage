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
        private readonly string _accessToken;
        private readonly HttpClient _httpClient;

        public TradovateRestApiClient(string apiUrl, string accessToken)
        {
            _apiUrl = apiUrl ?? throw new ArgumentNullException(nameof(apiUrl));
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
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
    }

    public class TradovateAccount
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int UserId { get; set; }
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
    }
}