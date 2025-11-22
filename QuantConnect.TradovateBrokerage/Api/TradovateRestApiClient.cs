using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        public int PlaceOrder(TradovateOrder order)
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

        private async Task<int> PlaceOrderAsync(TradovateOrder order)
        {
            try
            {
                var jsonContent = JsonConvert.SerializeObject(order);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiUrl}/order/placeorder", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var jsonResponse = JObject.Parse(responseBody);
                    return jsonResponse["orderId"]?.Value<int>() ?? 0;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        public bool CancelOrder(int orderId)
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

        private async Task<bool> CancelOrderAsync(int orderId)
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
        public decimal Balance { get; set; }
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
        public int AccountId { get; set; }
        public int ContractId { get; set; }
        public string Action { get; set; }
        public string OrderType { get; set; }
        public int Quantity { get; set; }
    }
}