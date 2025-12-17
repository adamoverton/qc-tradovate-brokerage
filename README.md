# Lean.Brokerages.Tradovate

[![Build Status](https://github.com/QuantConnect/Lean.Brokerages.Tradovate/workflows/Build%20%26%20Test/badge.svg)](https://github.com/QuantConnect/Lean.Brokerages.Tradovate/actions?query=workflow%3A%22Build+%26+Test%22)

This repository contains the Tradovate brokerage plugin for [QuantConnect LEAN](https://github.com/QuantConnect/Lean).

## Introduction

LEAN is an open-source algorithmic trading engine built for easy strategy research, backtesting, and live trading. This plugin enables LEAN to execute trades through Tradovate's futures trading platform.

### About Tradovate

[Tradovate](https://www.tradovate.com/) is a cloud-based futures trading platform offering commission-free futures trading with competitive margins. Tradovate supports trading on major futures exchanges including CME, CBOT, NYMEX, COMEX, and NYBOT.

**Key Features:**
- Commission-free futures trading (subscription-based pricing)
- Low day trading margins
- Cloud-based platform accessible from any device
- Popular with prop firm traders

## Using the Brokerage Plugin

### Configuration

The Tradovate brokerage supports two authentication methods:

#### OAuth Token Authentication (Recommended)

```json
{
    "environment": "tradovate-demo",
    "tradovate-oauth-token": "your-oauth-token",
    "tradovate-account-name": "DEMO1234567"
}
```

#### Client Credentials Authentication

```json
{
    "environment": "tradovate-demo",
    "tradovate-username": "your-username",
    "tradovate-password": "your-password",
    "tradovate-client-id": "your-app-id",
    "tradovate-client-secret": "your-client-secret",
    "tradovate-account-name": "DEMO1234567"
}
```

### Environment Settings

| Environment | Description |
|-------------|-------------|
| `tradovate-demo` | Demo/simulation trading |
| `tradovate-live` | Live trading with real funds |

### Account Selection

If you have multiple trading accounts (common with prop firms), specify the account using the `tradovate-account-name` parameter. The brokerage will list available accounts during initialization.

## Account Types

- **Individual Accounts**: Standard retail trading accounts
- **Prop Firm Accounts**: Multiple sub-accounts supported via account name selection

## Order Types

The following order types are supported:

| Order Type | Supported |
|------------|-----------|
| Market | Yes |
| Limit | Yes |
| Stop Market | Yes |
| Stop Limit | Yes |
| Trailing Stop | Yes (native Tradovate support) |

### Bracket Orders

Tradovate supports native bracket orders via Order Strategies. The brokerage includes API support for bracket orders (`StartOrderStrategy`/`CancelOrderStrategy`), though LEAN integration uses the standard linked order approach via `ContingentId`.

## Asset Classes

| Asset Class | Supported |
|-------------|-----------|
| Futures | Yes |
| Future Options | Planned |

### Supported Exchanges

- CME (E-mini S&P, E-mini NASDAQ, etc.)
- CBOT (Treasury futures, grains)
- NYMEX (Crude oil, natural gas)
- COMEX (Gold, silver)
- NYBOT (Coffee, sugar, cocoa)

## Brokerage Model

The `TradovateBrokerageModel` is included in LEAN's Common library and provides order validation and fee calculations.

### Fees

The `TradovateFeeModel` implements Tradovate's per-contract fee structure:

| Contract Type | Fee per Contract |
|---------------|------------------|
| Micro futures (MYM, MES, MNQ, M2K, etc.) | $0.79 |
| E-mini futures (YM, ES, NQ, RTY) | $1.29 |
| Standard futures (ZB, ZN, CL, GC, etc.) | $1.79 |

*Note: These are execution fees. Tradovate's subscription plans may offer different pricing.*

### Margin

The brokerage uses LEAN's built-in `FutureMarginModel` for margin calculations. Actual margin requirements are determined by Tradovate based on your account type and the specific contracts traded.

### Slippage

No slippage is modeled in backtests. Live orders experience actual market slippage based on order book conditions.

### Fills

- **Partial Fills**: Fully supported. The brokerage correctly handles Tradovate's cumulative fill reporting by converting to incremental fills for LEAN.
- **Market Orders**: Filled at best available price
- **Limit Orders**: Filled when price reaches limit level

## Known Limitations

### Execution-Only Brokerage

**This brokerage is execution-only and does not provide market data.**

The CME requires a sub-vendor license (~$290-375/month per exchange) for API market data distribution. Since Tradovate is popular with budget-conscious traders, this cost is prohibitive for most users.

**Solution**: Use a separate data feed provider:
- [Databento](https://databento.com/) - Professional futures data
- [IQFeed](https://www.iqfeed.net/) - Real-time and historical data
- QuantConnect's built-in data feeds

### No Historical Data Downloads

Due to the same CME licensing restrictions, historical data cannot be downloaded through this brokerage.

## Building

```bash
dotnet build QuantConnect.TradovateBrokerage.sln
```

## Testing

```bash
dotnet test
```

The test suite includes 80+ unit tests covering:
- REST API client functionality
- WebSocket connection and authentication
- Symbol mapping for futures contracts
- Partial fill handling
- Order lifecycle management

## Contributions

Contributions are welcome! Please ensure:
- Code follows the existing style conventions
- All tests pass
- New features include appropriate test coverage

## Code of Conduct

Please refer to QuantConnect's [Code of Conduct](https://github.com/QuantConnect/Lean/blob/master/CODE_OF_CONDUCT.md).

## License

This project is licensed under the Apache License 2.0. See the [LICENSE](LICENSE) file for details.
