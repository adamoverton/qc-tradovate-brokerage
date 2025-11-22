using NUnit.Framework;
using System;
using QuantConnect.Brokerages.Tradovate.Api;

namespace QuantConnect.Brokerages.Tradovate.Tests
{
    [TestFixture]
    public class TradovateWebSocketClientTests
    {
        private TradovateWebSocketClient _client;
        private const string TestUrl = "wss://demo-d.tradovateapi.com/v1/websocket";
        private const string TestAccessToken = "test_token";

        [SetUp]
        public void SetUp()
        {
            _client = new TradovateWebSocketClient(TestUrl, TestAccessToken);
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
        }

        [Test]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            var client = new TradovateWebSocketClient(TestUrl, TestAccessToken);
            Assert.IsNotNull(client);
            client.Dispose();
        }

        [Test]
        public void Constructor_NullUrl_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TradovateWebSocketClient(null, TestAccessToken));
        }

        [Test]
        public void Constructor_NullAccessToken_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TradovateWebSocketClient(TestUrl, null));
        }

        [Test]
        public void IsConnected_BeforeConnect_ReturnsFalse()
        {
            Assert.IsFalse(_client.IsConnected);
        }

        [Test]
        public void Connect_ValidParameters_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _client.Connect());
        }

        [Test]
        public void Disconnect_AfterConstruction_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _client.Disconnect());
        }

        [Test]
        public void SubscribeQuote_ValidSymbol_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _client.SubscribeQuote("ESM5"));
        }

        [Test]
        public void SubscribeQuote_NullSymbol_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _client.SubscribeQuote(null));
        }

        [Test]
        public void SubscribeQuote_EmptySymbol_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _client.SubscribeQuote(""));
        }

        [Test]
        public void UnsubscribeQuote_ValidSymbol_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _client.UnsubscribeQuote("ESM5"));
        }

        [Test]
        public void MessageReceived_EventExists()
        {
            var eventFired = false;
            _client.MessageReceived += (sender, message) => { eventFired = true; };
            Assert.IsFalse(eventFired);
        }

        [Test]
        public void ErrorOccurred_EventExists()
        {
            var eventFired = false;
            _client.ErrorOccurred += (sender, error) => { eventFired = true; };
            Assert.IsFalse(eventFired);
        }

        [Test]
        public void ConnectionStateChanged_EventExists()
        {
            var eventFired = false;
            _client.ConnectionStateChanged += (sender, isConnected) => { eventFired = true; };
            Assert.IsFalse(eventFired);
        }
    }
}