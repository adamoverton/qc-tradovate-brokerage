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

        #region Micro Contracts

        [Test]
        public void GetLeanSymbol_MicroES_UsesSameExpirationDayAsES()
        {
            var mesSymbol = _mapper.GetLeanSymbol("MESH5", SecurityType.Future, Market.USA);
            var esSymbol = _mapper.GetLeanSymbol("ESH5", SecurityType.Future, Market.USA);

            // Both should have day 15 (ES expiration day)
            Assert.AreEqual(15, mesSymbol.ID.Date.Day);
            Assert.AreEqual(esSymbol.ID.Date.Day, mesSymbol.ID.Date.Day);
        }

        [Test]
        public void GetLeanSymbol_MicroYM_UsesSameExpirationDayAsYM()
        {
            var mymSymbol = _mapper.GetLeanSymbol("MYMH5", SecurityType.Future, Market.USA);
            var ymSymbol = _mapper.GetLeanSymbol("YMH5", SecurityType.Future, Market.USA);

            // Both should have day 15 (YM expiration day)
            Assert.AreEqual(15, mymSymbol.ID.Date.Day);
            Assert.AreEqual(ymSymbol.ID.Date.Day, mymSymbol.ID.Date.Day);
        }

        [Test]
        public void GetLeanSymbol_MicroNQ_UsesSameExpirationDayAsNQ()
        {
            var mnqSymbol = _mapper.GetLeanSymbol("MNQH5", SecurityType.Future, Market.USA);
            var nqSymbol = _mapper.GetLeanSymbol("NQH5", SecurityType.Future, Market.USA);

            // Both should have day 15 (NQ expiration day)
            Assert.AreEqual(15, mnqSymbol.ID.Date.Day);
            Assert.AreEqual(nqSymbol.ID.Date.Day, mnqSymbol.ID.Date.Day);
        }

        [Test]
        public void GetLeanSymbol_MicroCL_UsesSameExpirationDayAsCL()
        {
            var mclSymbol = _mapper.GetLeanSymbol("MCLH5", SecurityType.Future, Market.USA);
            var clSymbol = _mapper.GetLeanSymbol("CLH5", SecurityType.Future, Market.USA);

            // Both should have day 20 (CL expiration day)
            Assert.AreEqual(20, mclSymbol.ID.Date.Day);
            Assert.AreEqual(clSymbol.ID.Date.Day, mclSymbol.ID.Date.Day);
        }

        [Test]
        public void GetLeanSymbol_MicroGC_UsesSameExpirationDayAsGC()
        {
            var mgcSymbol = _mapper.GetLeanSymbol("MGCH5", SecurityType.Future, Market.USA);
            var gcSymbol = _mapper.GetLeanSymbol("GCH5", SecurityType.Future, Market.USA);

            // Both should have day 27 (GC expiration day)
            Assert.AreEqual(27, mgcSymbol.ID.Date.Day);
            Assert.AreEqual(gcSymbol.ID.Date.Day, mgcSymbol.ID.Date.Day);
        }

        #endregion

        #region Year Decade Handling

        [Test]
        public void GetLeanSymbol_YearInCurrentDecade_ReturnsCorrectYear()
        {
            // Test that year digit in current decade works
            var currentYear = DateTime.UtcNow.Year;
            var yearDigit = currentYear % 10;
            var symbol = _mapper.GetLeanSymbol($"ESH{yearDigit}", SecurityType.Future, Market.USA);

            // Should be current year (within the decade)
            Assert.AreEqual(currentYear, symbol.ID.Date.Year);
        }

        [Test]
        public void GetLeanSymbol_UnknownContract_DefaultsToDay15()
        {
            // Unknown contract should default to day 15
            var symbol = _mapper.GetLeanSymbol("ZZZZH5", SecurityType.Future, Market.USA);
            Assert.AreEqual(15, symbol.ID.Date.Day);
        }

        #endregion
    }
}