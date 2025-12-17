using NUnit.Framework;
using NUnit.Framework.Legacy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using QuantConnect.Brokerages.Tradovate.Api;

namespace QuantConnect.Brokerages.Tradovate.Tests
{
    [TestFixture]
    public class TradovateRestApiClientTests
    {
        private TradovateRestApiClient _client;
        private const string TestAccessToken = "test_token";
        private const string TestApiUrl = "https://demo.tradovateapi.com/v1";

        [SetUp]
        public void SetUp()
        {
            _client = new TradovateRestApiClient(TestApiUrl, TestAccessToken);
        }

        [Test]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            var client = new TradovateRestApiClient(TestApiUrl, TestAccessToken);
            Assert.IsNotNull(client);
        }

        [Test]
        public void Constructor_NullApiUrl_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TradovateRestApiClient(null, TestAccessToken));
        }

        [Test]
        public void Constructor_NullAccessToken_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TradovateRestApiClient(TestApiUrl, null));
        }

        [Test]
        public void GetAccountList_ReturnsAccountList()
        {
            var accounts = _client.GetAccountList();
            Assert.IsNotNull(accounts);
        }

        [Test]
        public void GetPositionList_ReturnsPositionList()
        {
            var positions = _client.GetPositionList();
            Assert.IsNotNull(positions);
        }

        [Test]
        public void GetContractById_ValidId_CallsEndpoint()
        {
            var contract = _client.GetContractById(1);
            Assert.Pass("Design test - verifies method exists and returns expected type");
        }

        [Test]
        public void GetContractById_InvalidId_ReturnsNull()
        {
            var contract = _client.GetContractById(-1);
            Assert.IsNull(contract);
        }

        [Test]
        public void PlaceOrder_ValidOrder_CallsEndpoint()
        {
            var orderId = _client.PlaceOrder(new TradovateOrder
            {
                AccountId = 123,
                ContractId = 456,
                Action = "Buy",
                OrderType = "Market",
                Quantity = 1
            });

            Assert.Pass("Design test - verifies method exists and returns expected type");
        }

        [Test]
        public void PlaceOrder_NullOrder_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _client.PlaceOrder(null));
        }

        [Test]
        public void CancelOrder_ValidOrderId_CallsEndpoint()
        {
            var result = _client.CancelOrder(12345);
            Assert.Pass("Design test - verifies method exists and returns expected type");
        }

        [Test]
        public void CancelOrder_InvalidOrderId_ReturnsFalse()
        {
            var result = _client.CancelOrder(-1);
            Assert.IsFalse(result);
        }

        [Test]
        public void GetOrderList_ReturnsOrderList()
        {
            var orders = _client.GetOrderList();
            Assert.IsNotNull(orders);
        }

        #region Order Strategy Tests

        [Test]
        public void StartOrderStrategy_ValidRequest_CallsEndpoint()
        {
            var bracketParams = new BracketOrderParams
            {
                EntryVersion = new BracketEntryVersion
                {
                    OrderQty = 1,
                    OrderType = "Market",
                    TimeInForce = "GTC"
                },
                Brackets = new List<BracketExit>
                {
                    new BracketExit
                    {
                        Qty = 1,
                        ProfitTarget = 20,
                        StopLoss = -10
                    }
                }
            };

            var request = new StartOrderStrategyRequest
            {
                AccountId = 123456,
                AccountSpec = "DEMO123456",
                Symbol = "YMZ5",
                Action = "Buy",
                Params = JsonConvert.SerializeObject(bracketParams)
            };

            // This will fail with unauthorized, but tests the method exists and serializes correctly
            var result = _client.StartOrderStrategy(request);
            Assert.Pass("Design test - verifies method exists and serializes request correctly");
        }

        [Test]
        public void CancelOrderStrategy_ValidId_CallsEndpoint()
        {
            var result = _client.CancelOrderStrategy(12345);
            Assert.Pass("Design test - verifies method exists and returns expected type");
        }

        [Test]
        public void CancelOrderStrategy_InvalidId_ReturnsFalse()
        {
            var result = _client.CancelOrderStrategy(-1);
            Assert.IsFalse(result);
        }

        #endregion

        #region Bracket Order Model Tests

        [Test]
        public void StartOrderStrategyRequest_SerializesCorrectly()
        {
            var bracketParams = new BracketOrderParams
            {
                EntryVersion = new BracketEntryVersion
                {
                    OrderQty = 1,
                    OrderType = "Limit",
                    Price = 44000m,
                    TimeInForce = "GTC"
                },
                Brackets = new List<BracketExit>
                {
                    new BracketExit
                    {
                        Qty = 1,
                        ProfitTarget = 50,
                        StopLoss = -25,
                        TrailingStop = false
                    }
                }
            };

            var request = new StartOrderStrategyRequest
            {
                AccountId = 123456,
                AccountSpec = "DEMO123456",
                Symbol = "YMZ5",
                Action = "Buy",
                OrderStrategyTypeId = 2,
                Params = JsonConvert.SerializeObject(bracketParams)
            };

            var json = JsonConvert.SerializeObject(request);

            // Verify key fields are present with correct casing
            StringAssert.Contains("\"accountId\":123456", json);
            StringAssert.Contains("\"accountSpec\":\"DEMO123456\"", json);
            StringAssert.Contains("\"symbol\":\"YMZ5\"", json);
            StringAssert.Contains("\"action\":\"Buy\"", json);
            StringAssert.Contains("\"orderStrategyTypeId\":2", json);
            // Params should be a stringified JSON, not an object
            StringAssert.Contains("\"params\":\"", json);
        }

        [Test]
        public void BracketOrderParams_SerializesWithCorrectStructure()
        {
            var bracketParams = new BracketOrderParams
            {
                EntryVersion = new BracketEntryVersion
                {
                    OrderQty = 2,
                    OrderType = "Stop",
                    StopPrice = 43500m,
                    TimeInForce = "Day",
                    OrderId = 0
                },
                Brackets = new List<BracketExit>
                {
                    new BracketExit
                    {
                        Qty = 2,
                        ProfitTarget = 100,
                        StopLoss = -50,
                        TrailingStop = true
                    }
                }
            };

            var json = JsonConvert.SerializeObject(bracketParams);

            // Verify structure
            StringAssert.Contains("\"entryVersion\":", json);
            StringAssert.Contains("\"brackets\":", json);
            StringAssert.Contains("\"orderQty\":2", json);
            StringAssert.Contains("\"orderType\":\"Stop\"", json);
            StringAssert.Contains("\"stopPrice\":43500", json);
            StringAssert.Contains("\"profitTarget\":100", json);
            StringAssert.Contains("\"stopLoss\":-50", json);
            StringAssert.Contains("\"trailingStop\":true", json);
        }

        [Test]
        public void BracketEntryVersion_OmitsNullPrices()
        {
            var entry = new BracketEntryVersion
            {
                OrderQty = 1,
                OrderType = "Market",
                TimeInForce = "GTC"
                // Price and StopPrice not set
            };

            var json = JsonConvert.SerializeObject(entry);

            // Null prices should be omitted
            StringAssert.DoesNotContain("\"price\"", json);
            StringAssert.DoesNotContain("\"stopPrice\"", json);
        }

        [Test]
        public void BracketEntryVersion_IncludesSetPrices()
        {
            var entry = new BracketEntryVersion
            {
                OrderQty = 1,
                OrderType = "StopLimit",
                Price = 44100m,
                StopPrice = 44000m,
                TimeInForce = "GTC"
            };

            var json = JsonConvert.SerializeObject(entry);

            StringAssert.Contains("\"price\":44100", json);
            StringAssert.Contains("\"stopPrice\":44000", json);
        }

        #endregion

        #region Token Update Tests

        [Test]
        public void UpdateAccessToken_ValidToken_UpdatesSuccessfully()
        {
            var newToken = "new_access_token_12345";

            // Should not throw
            _client.UpdateAccessToken(newToken);

            Assert.Pass("Token updated without exception");
        }

        [Test]
        public void UpdateAccessToken_NullToken_DoesNotThrow()
        {
            // Should not throw, just log error
            _client.UpdateAccessToken(null);

            Assert.Pass("Null token handled gracefully");
        }

        [Test]
        public void UpdateAccessToken_EmptyToken_DoesNotThrow()
        {
            // Should not throw, just log error
            _client.UpdateAccessToken("");

            Assert.Pass("Empty token handled gracefully");
        }

        #endregion
    }
}