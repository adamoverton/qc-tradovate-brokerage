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
                    appId = "QuantConnect LEAN",
                    appVersion = "1.0",
                    deviceId = "QuantConnect",
                    cid = _clientId,
                    sec = _clientSecret
                };

                var jsonContent = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                Console.WriteLine($"[TradovateAuth] POST {authUrl}");
                Console.WriteLine($"[TradovateAuth] Payload: {jsonContent}");

                var response = await _httpClient.PostAsync(authUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"[TradovateAuth] Status: {response.StatusCode}");
                Console.WriteLine($"[TradovateAuth] Response: {responseBody}");

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = JObject.Parse(responseBody);

                    _accessToken = jsonResponse["accessToken"]?.ToString();

                    if (!string.IsNullOrEmpty(_accessToken))
                    {
                        // Save token to file for reuse
                        var tokenPath = "/tmp/tradovate_token.txt";
                        try
                        {
                            System.IO.File.WriteAllText(tokenPath, _accessToken);
                            Console.WriteLine($"[TradovateAuth] Token saved to {tokenPath}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TradovateAuth] Failed to save token: {ex.Message}");
                        }
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TradovateAuth] Exception: {ex.Message}");
                Console.WriteLine($"[TradovateAuth] StackTrace: {ex.StackTrace}");
                return false;
            }
        }
    }
}