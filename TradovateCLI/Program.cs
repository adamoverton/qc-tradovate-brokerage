using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading.Tasks;
using QuantConnect.Brokerages.Tradovate;
using QuantConnect.Brokerages.Tradovate.Api;
using QuantConnect.Logging;
using QuantConnect.Util;
using QuantConnect.Data;
using QuantConnect.Interfaces;

namespace TradovateCLI
{
    class Program
    {
        private static TradovateBrokerage _brokerage;
        private static bool _isConnected = false;

        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("🚀 Tradovate CLI - Manual Testing Tool");
            Console.WriteLine("=====================================");

            var rootCommand = new RootCommand("CLI tool for testing Tradovate brokerage integration")
            {
                CreateConnectCommand(),
                CreateDisconnectCommand(),
                CreateGetAccountsCommand(),
                CreateGetPositionsCommand(),
                CreateGetOrdersCommand(),
                CreatePlaceOrderCommand(),
                CreateCancelOrderCommand(),
                CreateStatusCommand()
            };

            return await rootCommand.InvokeAsync(args);
        }

        static Command CreateConnectCommand()
        {
            var connectCommand = new Command("connect", "Connect to Tradovate");

            var usernameOption = new Option<string>("--username", "Tradovate username") { IsRequired = true };
            var passwordOption = new Option<string>("--password", "Tradovate password") { IsRequired = true };
            var apiKeyOption = new Option<string>("--apikey", "Tradovate API key") { IsRequired = true };
            var envOption = new Option<string>("--env", () => "demo", "Environment (demo/live)");

            connectCommand.AddOption(usernameOption);
            connectCommand.AddOption(passwordOption);
            connectCommand.AddOption(apiKeyOption);
            connectCommand.AddOption(envOption);

            connectCommand.SetHandler(async (string username, string password, string apiKey, string env) =>
            {
                try
                {
                    var environment = env.ToLower() == "live" ? TradovateEnvironment.Live : TradovateEnvironment.Demo;

                    Log.LogHandler = new ConsoleLogHandler();

                    _brokerage = new TradovateBrokerage(
                        Composer.Instance.GetPart<IDataAggregator>() ?? new DummyDataAggregator(),
                        username,
                        password,
                        apiKey,
                        environment
                    );

                    _brokerage.Message += OnBrokerageMessage;

                    Console.WriteLine($"🔌 Connecting to Tradovate ({env})...");
                    _brokerage.Connect();

                    await Task.Delay(2000); // Wait for connection

                    _isConnected = _brokerage.IsConnected;

                    if (_isConnected)
                    {
                        Console.WriteLine("✅ Connected successfully!");
                    }
                    else
                    {
                        Console.WriteLine("❌ Connection failed!");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }
            }, usernameOption, passwordOption, apiKeyOption, envOption);

            return connectCommand;
        }

        static Command CreateDisconnectCommand()
        {
            var disconnectCommand = new Command("disconnect", "Disconnect from Tradovate");

            disconnectCommand.SetHandler(() =>
            {
                if (_brokerage != null)
                {
                    Console.WriteLine("🔌 Disconnecting...");
                    _brokerage.Disconnect();
                    _isConnected = false;
                    Console.WriteLine("✅ Disconnected");
                }
                else
                {
                    Console.WriteLine("⚠️ Not connected");
                }
            });

            return disconnectCommand;
        }

        static Command CreateGetAccountsCommand()
        {
            var getAccountsCommand = new Command("get-accounts", "Get account information");

            getAccountsCommand.SetHandler(() =>
            {
                if (!CheckConnection()) return;

                try
                {
                    Console.WriteLine("📊 Getting accounts...");
                    var accounts = _brokerage.GetCashBalance();

                    if (accounts.Count == 0)
                    {
                        Console.WriteLine("No accounts found");
                        return;
                    }

                    Console.WriteLine("\n💰 Account Balances:");
                    foreach (var account in accounts)
                    {
                        Console.WriteLine($"  {account.Currency}: {account.Amount:F2}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }
            });

            return getAccountsCommand;
        }

        static Command CreateGetPositionsCommand()
        {
            var getPositionsCommand = new Command("get-positions", "Get current positions");

            getPositionsCommand.SetHandler(() =>
            {
                if (!CheckConnection()) return;

                try
                {
                    Console.WriteLine("📈 Getting positions...");
                    var positions = _brokerage.GetAccountHoldings();

                    if (positions.Count == 0)
                    {
                        Console.WriteLine("No positions found");
                        return;
                    }

                    Console.WriteLine("\n📊 Current Positions:");
                    foreach (var position in positions)
                    {
                        Console.WriteLine($"  {position.Symbol}: {position.Quantity} @ {position.AveragePrice:F2}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }
            });

            return getPositionsCommand;
        }

        static Command CreateGetOrdersCommand()
        {
            var getOrdersCommand = new Command("get-orders", "Get open orders");

            getOrdersCommand.SetHandler(() =>
            {
                if (!CheckConnection()) return;

                try
                {
                    Console.WriteLine("📋 Getting orders...");
                    var orders = _brokerage.GetOpenOrders();

                    if (orders.Count == 0)
                    {
                        Console.WriteLine("No open orders found");
                        return;
                    }

                    Console.WriteLine("\n📋 Open Orders:");
                    foreach (var order in orders)
                    {
                        Console.WriteLine($"  Order ID: {order.Id}, Symbol: {order.Symbol}, Quantity: {order.Quantity}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }
            });

            return getOrdersCommand;
        }

        static Command CreatePlaceOrderCommand()
        {
            var placeOrderCommand = new Command("place-order", "Place a new order");

            var symbolOption = new Option<string>("--symbol", "Symbol (e.g., ES, NQ)") { IsRequired = true };
            var sideOption = new Option<string>("--side", "Order side (buy/sell)") { IsRequired = true };
            var quantityOption = new Option<int>("--quantity", "Quantity") { IsRequired = true };
            var typeOption = new Option<string>("--type", () => "market", "Order type (market/limit)");

            placeOrderCommand.AddOption(symbolOption);
            placeOrderCommand.AddOption(sideOption);
            placeOrderCommand.AddOption(quantityOption);
            placeOrderCommand.AddOption(typeOption);

            placeOrderCommand.SetHandler((string symbol, string side, int quantity, string type) =>
            {
                if (!CheckConnection()) return;

                try
                {
                    Console.WriteLine($"📝 Placing {side} order: {quantity} {symbol} ({type})");

                    // For now, just test that the method exists
                    Console.WriteLine("⚠️ Order placement temporarily disabled for safety - TDD implementation in progress");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }
            }, symbolOption, sideOption, quantityOption, typeOption);

            return placeOrderCommand;
        }

        static Command CreateCancelOrderCommand()
        {
            var cancelOrderCommand = new Command("cancel-order", "Cancel an order");

            var orderIdOption = new Option<string>("--order-id", "Order ID to cancel") { IsRequired = true };

            cancelOrderCommand.AddOption(orderIdOption);

            cancelOrderCommand.SetHandler((string orderId) =>
            {
                if (!CheckConnection()) return;

                try
                {
                    Console.WriteLine($"❌ Canceling order: {orderId}");
                    Console.WriteLine("⚠️ Order cancellation temporarily disabled for safety - TDD implementation in progress");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }
            }, orderIdOption);

            return cancelOrderCommand;
        }

        static Command CreateStatusCommand()
        {
            var statusCommand = new Command("status", "Show connection status");

            statusCommand.SetHandler(() =>
            {
                Console.WriteLine($"🔌 Connection Status: {(_isConnected ? "✅ Connected" : "❌ Disconnected")}");
                if (_brokerage != null)
                {
                    Console.WriteLine($"📡 Brokerage Status: {(_brokerage.IsConnected ? "✅ Active" : "❌ Inactive")}");
                }
            });

            return statusCommand;
        }

        static bool CheckConnection()
        {
            if (!_isConnected || _brokerage == null || !_brokerage.IsConnected)
            {
                Console.WriteLine("❌ Not connected. Use 'connect' command first.");
                return false;
            }
            return true;
        }

        static void OnBrokerageMessage(object sender, QuantConnect.Brokerages.BrokerageMessageEvent e)
        {
            var icon = e.Type switch
            {
                QuantConnect.Brokerages.BrokerageMessageType.Information => "ℹ️",
                QuantConnect.Brokerages.BrokerageMessageType.Warning => "⚠️",
                QuantConnect.Brokerages.BrokerageMessageType.Error => "❌",
                _ => "📢"
            };

            Console.WriteLine($"{icon} {e.Code}: {e.Message}");
        }
    }

    // Dummy implementation for CLI testing
    public class DummyDataAggregator : IDataAggregator
    {
        public IEnumerator<BaseData> Add(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            yield break;
        }

        public bool Remove(SubscriptionDataConfig dataConfig) => true;
        public void Initialize(DataAggregatorInitializeParameters parameters) { }
        public void Update(BaseData data) { }
        public void Dispose() { }
    }

    public class ConsoleLogHandler : ILogHandler
    {
        public void Error(string text) => Console.WriteLine($"[ERROR] {text}");
        public void Debug(string text) => Console.WriteLine($"[DEBUG] {text}");
        public void Trace(string text) => Console.WriteLine($"[TRACE] {text}");
        public void Dispose() { }
    }
}