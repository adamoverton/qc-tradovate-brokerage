using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantConnect.Logging;

namespace QuantConnect.Brokerages.Tradovate.Api
{
    public class TradovateAuthManager : IDisposable
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
        private DateTime _tokenExpiration;
        private Timer _refreshTimer;
        private bool _disposed;

        // Track consecutive failures for external handlers
        private int _consecutiveRefreshFailures;
        private const int MaxRefreshFailuresBeforeEvent = 3;

        // Refresh token 60 seconds before expiration (Tradovate recommends 30s, we use 60s for safety)
        private const int RefreshBufferSeconds = 60;
        // Default token lifetime is ~80 minutes, but we track actual expiration from API response
        private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromMinutes(75);

        /// <summary>
        /// Event raised when the access token is refreshed
        /// </summary>
        public event EventHandler<TokenRefreshedEventArgs> TokenRefreshed;

        /// <summary>
        /// Event raised when authentication fails after multiple retry attempts.
        /// External handlers can use this to fetch a new token and call UpdateToken().
        /// </summary>
        public event EventHandler<AuthenticationFailedEventArgs> AuthenticationFailed;

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
            _tokenExpiration = DateTime.MinValue;
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
            // For OAuth tokens, set default expiration - will be updated when we refresh
            _tokenExpiration = DateTime.UtcNow.Add(DefaultTokenLifetime);
            _consecutiveRefreshFailures = 0;
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

        /// <summary>
        /// Gets the current access token
        /// </summary>
        public string GetAccessToken()
        {
            return _accessToken;
        }

        /// <summary>
        /// Gets the token expiration time in UTC
        /// </summary>
        public DateTime TokenExpiration => _tokenExpiration;

        /// <summary>
        /// Updates the access token with a new value from an external source.
        /// Use this when the AuthenticationFailed event is raised to provide a fresh token.
        /// </summary>
        /// <param name="newToken">The new OAuth token to use</param>
        /// <returns>True if the token was updated and renewal succeeded</returns>
        public bool UpdateToken(string newToken)
        {
            if (string.IsNullOrEmpty(newToken))
            {
                Log.Error("TradovateAuthManager.UpdateToken(): New token is null or empty");
                return false;
            }

            Log.Trace("TradovateAuthManager.UpdateToken(): Updating access token from external source");
            _accessToken = newToken;
            _tokenExpiration = DateTime.UtcNow.Add(DefaultTokenLifetime);
            _consecutiveRefreshFailures = 0;

            // Try to renew immediately to get proper expiration and validate the token
            var renewSuccess = RenewAccessToken();
            if (renewSuccess)
            {
                Log.Trace($"TradovateAuthManager.UpdateToken(): Token validated, expires at {_tokenExpiration:yyyy-MM-dd HH:mm:ss} UTC");

                // Reschedule the refresh timer
                var timeUntilRefresh = CalculateTimeUntilRefresh();
                if (timeUntilRefresh > TimeSpan.Zero && _refreshTimer != null)
                {
                    _refreshTimer.Change(timeUntilRefresh, Timeout.InfiniteTimeSpan);
                    Log.Trace($"TradovateAuthManager.UpdateToken(): Next refresh scheduled in {timeUntilRefresh.TotalMinutes:F1} minutes");
                }
            }
            else
            {
                Log.Error("TradovateAuthManager.UpdateToken(): Token renewal failed - token may be invalid");
            }

            return renewSuccess;
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
                    appId = "qctest",
                    appVersion = "1.0",
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

        /// <summary>
        /// Starts the automatic token refresh timer.
        /// Should be called after successful authentication.
        /// </summary>
        public void StartAutoRefresh()
        {
            if (_refreshTimer != null)
            {
                Log.Trace("TradovateAuthManager.StartAutoRefresh(): Timer already running");
                return;
            }

            // For OAuth tokens, we don't know the actual expiration time.
            // Always refresh immediately on startup to get a fresh token with known expiration.
            // This handles the case where the algorithm restarts multiple times with the same
            // initial OAuth token - each restart would otherwise assume 75 minutes remaining.
            TimeSpan timeUntilRefresh;
            if (_useOAuth)
            {
                Log.Trace("TradovateAuthManager.StartAutoRefresh(): OAuth mode - refreshing immediately to get known expiration");
                var success = RenewAccessToken();
                if (!success)
                {
                    _consecutiveRefreshFailures++;
                    Log.Error($"TradovateAuthManager.StartAutoRefresh(): Initial token refresh failed - token is expired");

                    // Token is expired on startup - raise event and exit (no retry, auth failure is fatal)
                    Log.Trace("TradovateAuthManager.StartAutoRefresh(): Token expired - raising AuthenticationFailed event (fatal, no retry)");
                    try
                    {
                        AuthenticationFailed?.Invoke(this, new AuthenticationFailedEventArgs(
                            _consecutiveRefreshFailures,
                            "OAuth token expired on startup and cannot be renewed"));
                    }
                    catch (Exception eventEx)
                    {
                        Log.Error($"TradovateAuthManager.StartAutoRefresh(): Exception in AuthenticationFailed handler: {eventEx.Message}");
                    }

                    // Don't schedule retry timer - auth failure is fatal, algorithm should exit
                    return;
                }
                else
                {
                    _consecutiveRefreshFailures = 0;
                    // Calculate when to refresh (before expiration)
                    timeUntilRefresh = CalculateTimeUntilRefresh();
                    if (timeUntilRefresh <= TimeSpan.Zero)
                    {
                        timeUntilRefresh = TimeSpan.FromSeconds(30);
                    }
                }
            }
            else
            {
                // Calculate when to refresh (before expiration)
                timeUntilRefresh = CalculateTimeUntilRefresh();
                if (timeUntilRefresh <= TimeSpan.Zero)
                {
                    // Token already expired or about to expire, refresh soon
                    timeUntilRefresh = TimeSpan.FromSeconds(30);
                }
            }

            Log.Trace($"TradovateAuthManager.StartAutoRefresh(): Token expires at {_tokenExpiration:yyyy-MM-dd HH:mm:ss} UTC, scheduling refresh in {timeUntilRefresh.TotalMinutes:F1} minutes");

            _refreshTimer = new Timer(
                OnRefreshTimerCallback,
                null,
                timeUntilRefresh,
                Timeout.InfiniteTimeSpan // Don't repeat, we'll reschedule after each refresh
            );
        }

        /// <summary>
        /// Stops the automatic token refresh timer
        /// </summary>
        public void StopAutoRefresh()
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Dispose();
                _refreshTimer = null;
                Log.Trace("TradovateAuthManager.StopAutoRefresh(): Timer stopped");
            }
        }

        private TimeSpan CalculateTimeUntilRefresh()
        {
            var timeUntilExpiration = _tokenExpiration - DateTime.UtcNow;
            var timeUntilRefresh = timeUntilExpiration - TimeSpan.FromSeconds(RefreshBufferSeconds);
            return timeUntilRefresh;
        }

        private void OnRefreshTimerCallback(object state)
        {
            try
            {
                Log.Trace("TradovateAuthManager: Token refresh timer triggered");
                var success = RenewAccessToken();

                if (success)
                {
                    _consecutiveRefreshFailures = 0;
                    // Reschedule for the next refresh
                    var timeUntilRefresh = CalculateTimeUntilRefresh();
                    if (timeUntilRefresh > TimeSpan.Zero)
                    {
                        Log.Trace($"TradovateAuthManager: Next refresh scheduled in {timeUntilRefresh.TotalMinutes:F1} minutes");
                        _refreshTimer?.Change(timeUntilRefresh, Timeout.InfiniteTimeSpan);
                    }
                }
                else
                {
                    _consecutiveRefreshFailures++;
                    Log.Error($"TradovateAuthManager: Token refresh failed ({_consecutiveRefreshFailures}/{MaxRefreshFailuresBeforeEvent})");

                    // After multiple failures, raise event so external handlers can provide a new token
                    if (_consecutiveRefreshFailures >= MaxRefreshFailuresBeforeEvent)
                    {
                        Log.Trace("TradovateAuthManager: Multiple refresh failures - raising AuthenticationFailed event");
                        try
                        {
                            AuthenticationFailed?.Invoke(this, new AuthenticationFailedEventArgs(
                                _consecutiveRefreshFailures,
                                "Token refresh failed after multiple attempts"));
                        }
                        catch (Exception eventEx)
                        {
                            Log.Error($"TradovateAuthManager: Exception in AuthenticationFailed handler: {eventEx.Message}");
                        }
                    }

                    // Retry in 30 seconds
                    Log.Trace("TradovateAuthManager: Retrying in 30 seconds");
                    _refreshTimer?.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateAuthManager: Exception in refresh timer: {ex.Message}");
                _consecutiveRefreshFailures++;
                // Retry in 30 seconds
                _refreshTimer?.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Renews the access token using the /auth/renewaccesstoken endpoint.
        /// Must be called before the current token expires.
        /// </summary>
        /// <returns>True if renewal succeeded, false otherwise</returns>
        public bool RenewAccessToken()
        {
            try
            {
                return RenewAccessTokenAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateAuthManager.RenewAccessToken(): Exception: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RenewAccessTokenAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                Log.Error("TradovateAuthManager.RenewAccessToken(): No access token to renew");
                return false;
            }

            try
            {
                var renewUrl = $"{GetApiUrl()}/auth/renewaccesstoken";

                // Create request with current token in Authorization header
                var request = new HttpRequestMessage(HttpMethod.Get, renewUrl);
                request.Headers.Add("Authorization", $"Bearer {_accessToken}");

                Log.Trace($"TradovateAuthManager.RenewAccessToken(): GET {renewUrl}");

                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                Log.Trace($"TradovateAuthManager.RenewAccessToken(): Status: {response.StatusCode}");
                Log.Trace($"TradovateAuthManager.RenewAccessToken(): Response: {responseBody}");

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = JObject.Parse(responseBody);

                    var newAccessToken = jsonResponse["accessToken"]?.ToString();
                    var expirationTimeStr = jsonResponse["expirationTime"]?.ToString();

                    if (!string.IsNullOrEmpty(newAccessToken))
                    {
                        var oldToken = _accessToken;
                        _accessToken = newAccessToken;

                        // Parse expiration time if provided
                        if (!string.IsNullOrEmpty(expirationTimeStr) && DateTime.TryParse(expirationTimeStr, out var expirationTime))
                        {
                            _tokenExpiration = expirationTime.ToUniversalTime();
                        }
                        else
                        {
                            // Use default lifetime if not provided
                            _tokenExpiration = DateTime.UtcNow.Add(DefaultTokenLifetime);
                        }

                        Log.Trace($"TradovateAuthManager.RenewAccessToken(): Success! New expiration: {_tokenExpiration:yyyy-MM-dd HH:mm:ss} UTC");

                        // Raise event so REST and WebSocket clients can update their tokens
                        TokenRefreshed?.Invoke(this, new TokenRefreshedEventArgs(newAccessToken, _tokenExpiration));

                        return true;
                    }
                    else
                    {
                        Log.Error("TradovateAuthManager.RenewAccessToken(): Response missing accessToken");
                    }
                }
                else
                {
                    Log.Error($"TradovateAuthManager.RenewAccessToken(): Failed with status {response.StatusCode}: {responseBody}");
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"TradovateAuthManager.RenewAccessToken(): Exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disposes resources used by the auth manager
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    StopAutoRefresh();
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Event args for the TokenRefreshed event
    /// </summary>
    public class TokenRefreshedEventArgs : EventArgs
    {
        public string NewAccessToken { get; }
        public DateTime Expiration { get; }

        public TokenRefreshedEventArgs(string newAccessToken, DateTime expiration)
        {
            NewAccessToken = newAccessToken;
            Expiration = expiration;
        }
    }

    /// <summary>
    /// Event args for the AuthenticationFailed event
    /// </summary>
    public class AuthenticationFailedEventArgs : EventArgs
    {
        /// <summary>
        /// Number of consecutive refresh failures
        /// </summary>
        public int FailureCount { get; }

        /// <summary>
        /// Description of the failure reason
        /// </summary>
        public string Reason { get; }

        public AuthenticationFailedEventArgs(int failureCount, string reason)
        {
            FailureCount = failureCount;
            Reason = reason;
        }
    }
}