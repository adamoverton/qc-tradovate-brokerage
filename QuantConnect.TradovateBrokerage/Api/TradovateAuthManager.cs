using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace QuantConnect.Brokerages.Tradovate.Api
{
    public class TradovateAuthManager
    {
        private readonly string _username;
        private readonly string _password;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _oauthToken;
        private readonly TradovateEnvironment _environment;
        private readonly HttpClient _httpClient;
        private readonly bool _useOAuth;
        private string _accessToken;

        public TradovateAuthManager(
            string username,
            string password,
            string clientId,
            string clientSecret,
            TradovateEnvironment environment)
        {
            _username = username ?? throw new ArgumentNullException(nameof(username));
            _password = password ?? throw new ArgumentNullException(nameof(password));
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
            _environment = environment;
            _httpClient = new HttpClient();
            _useOAuth = false;
        }

        public TradovateAuthManager(
            string oauthToken,
            TradovateEnvironment environment)
        {
            _oauthToken = oauthToken ?? throw new ArgumentNullException(nameof(oauthToken));
            _environment = environment;
            _httpClient = new HttpClient();
            _useOAuth = true;
            _accessToken = oauthToken;
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

        public string GetAccessToken()
        {
            return _accessToken;
        }

        public string GetApiUrl()
        {
            return _environment == TradovateEnvironment.Demo
                ? "https://demo.tradovateapi.com/v1"
                : "https://live.tradovateapi.com/v1";
        }

        public bool Authenticate()
        {
            if (_useOAuth)
            {
                return IsAuthenticated;
            }

            try
            {
                var result = AuthenticateAsync().GetAwaiter().GetResult();
                return result;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> AuthenticateAsync()
        {
            try
            {
                var authUrl = $"{GetApiUrl()}/auth/accesstokenrequest";

                var payload = new
                {
                    name = _username,
                    password = _password,
                    appId = _clientId,
                    appVersion = "1.0",
                    deviceId = "QuantConnect",
                    cid = _clientId,
                    sec = _clientSecret
                };

                var jsonContent = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(authUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var jsonResponse = JObject.Parse(responseBody);

                    _accessToken = jsonResponse["accessToken"]?.ToString();

                    return !string.IsNullOrEmpty(_accessToken);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}