using NUnit.Framework;
using System;
using QuantConnect.Brokerages.Tradovate;

namespace QuantConnect.Brokerages.Tradovate.Tests
{
    [TestFixture]
    public class TradovateSymbolMapperTests
    {
        private TradovateSymbolMapper _mapper;

        [SetUp]
        public void SetUp()
        {
            _mapper = new TradovateSymbolMapper();
        }

        [Test]
        public void GetBrokerageSymbol_ESFuture_ReturnsCorrectFormat()
        {
            var symbol = Symbol.CreateFuture("ES", Market.USA, new DateTime(2025, 6, 15));
            var brokerageSymbol = _mapper.GetBrokerageSymbol(symbol);
            Assert.AreEqual("ESM5", brokerageSymbol);
        }

        [Test]
        public void GetBrokerageSymbol_NQFuture_ReturnsCorrectFormat()
        {
            var symbol = Symbol.CreateFuture("NQ", Market.USA, new DateTime(2025, 3, 15));
            var brokerageSymbol = _mapper.GetBrokerageSymbol(symbol);
            Assert.AreEqual("NQH5", brokerageSymbol);
        }

        [Test]
        public void GetBrokerageSymbol_NullSymbol_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _mapper.GetBrokerageSymbol(null));
        }

        [Test]
        public void GetLeanSymbol_ValidESSymbol_ReturnsLeanSymbol()
        {
            var leanSymbol = _mapper.GetLeanSymbol("ESM5", SecurityType.Future, Market.USA);

            Assert.IsNotNull(leanSymbol);
            Assert.AreEqual("ES", leanSymbol.ID.Symbol);
            Assert.AreEqual(SecurityType.Future, leanSymbol.SecurityType);
            Assert.AreEqual(new DateTime(2025, 6, 15), leanSymbol.ID.Date);
        }

        [Test]
        public void GetLeanSymbol_ValidNQSymbol_ReturnsLeanSymbol()
        {
            var leanSymbol = _mapper.GetLeanSymbol("NQH5", SecurityType.Future, Market.USA);

            Assert.IsNotNull(leanSymbol);
            Assert.AreEqual("NQ", leanSymbol.ID.Symbol);
            Assert.AreEqual(SecurityType.Future, leanSymbol.SecurityType);
            Assert.AreEqual(new DateTime(2025, 3, 15), leanSymbol.ID.Date);
        }

        [Test]
        public void GetLeanSymbol_InvalidFormat_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _mapper.GetLeanSymbol("INVALID", SecurityType.Future, Market.USA));
        }

        [Test]
        public void GetLeanSymbol_NullBrokerageSymbol_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _mapper.GetLeanSymbol(null, SecurityType.Future, Market.USA));
        }

        [Test]
        public void GetLeanSymbol_EmptyBrokerageSymbol_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _mapper.GetLeanSymbol("", SecurityType.Future, Market.USA));
        }

        [TestCase("F", 1, 15)]
        [TestCase("G", 2, 15)]
        [TestCase("H", 3, 15)]
        [TestCase("J", 4, 15)]
        [TestCase("K", 5, 15)]
        [TestCase("M", 6, 15)]
        [TestCase("N", 7, 15)]
        [TestCase("Q", 8, 15)]
        [TestCase("U", 9, 15)]
        [TestCase("V", 10, 15)]
        [TestCase("X", 11, 15)]
        [TestCase("Z", 12, 15)]
        public void MonthCodeMapping_AllMonths_CorrectlyMapped(string monthCode, int month, int day)
        {
            var expectedDate = new DateTime(2025, month, day);
            var symbol = Symbol.CreateFuture("ES", Market.USA, expectedDate);
            var brokerageSymbol = _mapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual($"ES{monthCode}5", brokerageSymbol);
        }
    }
}