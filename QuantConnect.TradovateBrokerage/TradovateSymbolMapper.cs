using System;
using System.Collections.Generic;
using System.Globalization;

namespace QuantConnect.Brokerages.Tradovate
{
    public class TradovateSymbolMapper : ISymbolMapper
    {
        private static readonly Dictionary<int, string> _monthCodes = new Dictionary<int, string>
        {
            { 1, "F" },
            { 2, "G" },
            { 3, "H" },
            { 4, "J" },
            { 5, "K" },
            { 6, "M" },
            { 7, "N" },
            { 8, "Q" },
            { 9, "U" },
            { 10, "V" },
            { 11, "X" },
            { 12, "Z" }
        };

        private static readonly Dictionary<string, int> _reverseMonthCodes = new Dictionary<string, int>
        {
            { "F", 1 },
            { "G", 2 },
            { "H", 3 },
            { "J", 4 },
            { "K", 5 },
            { "M", 6 },
            { "N", 7 },
            { "Q", 8 },
            { "U", 9 },
            { "V", 10 },
            { "X", 11 },
            { "Z", 12 }
        };

        private static readonly Dictionary<string, int> _expirationDays = new Dictionary<string, int>
        {
            { "ES", 15 },
            { "NQ", 15 },
            { "YM", 15 },
            { "RTY", 15 },
            { "CL", 20 },
            { "GC", 27 },
            { "SI", 27 }
        };

        public string GetBrokerageSymbol(Symbol symbol)
        {
            if (symbol == null || string.IsNullOrWhiteSpace(symbol.Value))
            {
                throw new ArgumentException("Invalid symbol");
            }

            if (symbol.SecurityType != SecurityType.Future)
            {
                throw new ArgumentException($"Unsupported security type: {symbol.SecurityType}");
            }

            var ticker = symbol.ID.Symbol;
            var expirationDate = symbol.ID.Date;
            var month = expirationDate.Month;
            var year = expirationDate.Year % 10;

            if (!_monthCodes.TryGetValue(month, out var monthCode))
            {
                throw new ArgumentException($"Invalid expiration month: {month}");
            }

            return $"{ticker}{monthCode}{year}";
        }

        public Symbol GetLeanSymbol(string brokerageSymbol, SecurityType securityType, string market, DateTime expirationDate = default(DateTime), decimal strike = 0, OptionRight optionRight = 0)
        {
            if (string.IsNullOrWhiteSpace(brokerageSymbol))
            {
                throw new ArgumentException("Invalid brokerage symbol");
            }

            if (securityType != SecurityType.Future)
            {
                throw new ArgumentException($"Unsupported security type: {securityType}");
            }

            if (brokerageSymbol.Length < 3)
            {
                throw new ArgumentException($"Invalid symbol format: {brokerageSymbol}");
            }

            var yearDigit = brokerageSymbol.Substring(brokerageSymbol.Length - 1);
            var monthCode = brokerageSymbol.Substring(brokerageSymbol.Length - 2, 1);
            var ticker = brokerageSymbol.Substring(0, brokerageSymbol.Length - 2);

            if (!_reverseMonthCodes.TryGetValue(monthCode, out var month))
            {
                throw new ArgumentException($"Invalid month code: {monthCode}");
            }

            if (!int.TryParse(yearDigit, out var year))
            {
                throw new ArgumentException($"Invalid year digit: {yearDigit}");
            }

            var fullYear = 2020 + year;

            var day = 15;
            if (_expirationDays.TryGetValue(ticker, out var expirationDay))
            {
                day = expirationDay;
            }

            var contractExpiration = new DateTime(fullYear, month, day);

            return Symbol.CreateFuture(ticker, market, contractExpiration);
        }
    }
}