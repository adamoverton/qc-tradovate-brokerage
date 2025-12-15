# Tradovate Order Lifecycle Implementation Plan

## Overview

Implement complete order lifecycle support: place order, receive notifications, cancel order, verify cancellation.

## Current State

### What We Have
1. **PlaceOrder** - Working, returns broker order ID
2. **CancelOrder** - REST API call exists, but no event fired
3. **GetOpenOrders** - Implemented, reads order list via REST
4. **WebSocket** - Connected and authenticated, receives messages but doesn't process them

### What's Missing
1. **Order event notifications** - WebSocket receives messages but doesn't fire QC OrderEvent
2. **Cancel event** - CancelOrder doesn't fire OrderEvent when complete
3. **Test coverage** - No end-to-end test verifying full lifecycle

## Tradovate API Summary

### WebSocket Subscription (user/syncrequest)
```
// Subscribe format (newline-delimited)
user/syncrequest
{requestId}

{"users":[{userId}]}
```

### Event Message Format
```json
{
  "e": "props",
  "d": {
    "entityType": "order",      // or "executionReport", "fill", etc.
    "eventType": "Created",     // or "Updated", "Deleted"
    "entity": {
      "id": 296445350043,
      "accountId": 30210450,
      "contractId": 4214513,
      "ordStatus": "Working",   // Working, Filled, Cancelled, Rejected
      "action": "Buy",
      "orderType": "Limit",
      "price": 43900,
      "qty": 1,
      "filledQty": 0,
      "avgFillPrice": null
    }
  }
}
```

### Order Status Values (ordStatus)
- `Working` - Order is active, can be cancelled
- `Filled` - Order completely filled
- `Cancelled` - Order was cancelled
- `Rejected` - Order was rejected
- `PendingNew` - Order pending acceptance
- `PendingCancel` - Cancel pending
- `Expired` - Order expired

### QC OrderStatus Mapping
| Tradovate ordStatus | QC OrderStatus |
|---------------------|----------------|
| Working             | Submitted      |
| PendingNew          | Submitted      |
| Filled              | Filled         |
| Cancelled           | Canceled       |
| PendingCancel       | CancelPending  |
| Rejected            | Invalid        |
| Expired             | Canceled       |

## Implementation Plan

### 1. WebSocket: Subscribe to user/syncrequest

**File**: `TradovateWebSocketClient.cs`

Add method to subscribe:
```csharp
public void SubscribeUserSync(int userId)
{
    var subscribeMessage = $"user/syncrequest\n{_requestId++}\n\n{{\"users\":[{userId}]}}";
    SendAsync(subscribeMessage).GetAwaiter().GetResult();
}
```

### 2. WebSocket: Parse Order Events

**File**: `TradovateWebSocketClient.cs`

Add event types:
```csharp
public event EventHandler<TradovateOrderEvent> OrderEventReceived;

public class TradovateOrderEvent
{
    public string EventType { get; set; }  // Created, Updated, Deleted
    public long OrderId { get; set; }
    public string OrdStatus { get; set; }
    public int FilledQty { get; set; }
    public decimal? AvgFillPrice { get; set; }
    public string Action { get; set; }
    public int ContractId { get; set; }
}
```

In ProcessArrayFrame, detect order events:
```csharp
if (entityType == "order" || entityType == "executionReport")
{
    var orderEvent = ParseOrderEvent(entity);
    OrderEventReceived?.Invoke(this, orderEvent);
}
```

### 3. Brokerage: Handle Order Events

**File**: `TradovateBrokerage.cs`

In Connect(), after WebSocket connects:
```csharp
_webSocketClient.OrderEventReceived += OnTradovateOrderEvent;

// Subscribe to user events
var accountId = GetAccountId();
_webSocketClient.SubscribeUserSync(accountId);
```

Add handler:
```csharp
private void OnTradovateOrderEvent(object sender, TradovateOrderEvent tvEvent)
{
    // Find the QC order by broker ID
    // Map status and fire OnOrderEvent
}
```

### 4. Brokerage: Fire Cancel Event

**File**: `TradovateBrokerage.cs`

Update CancelOrder to fire event on success:
```csharp
public override bool CancelOrder(Order order)
{
    // ... existing code ...

    var success = _restClient.CancelOrder(brokerId);
    if (success)
    {
        OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero, "Order cancel submitted")
        {
            Status = OrderStatus.CancelPending
        });
    }
    return success;
}
```

Note: The actual Cancelled status will come via WebSocket when Tradovate confirms.

### 5. REST API: Verify GetOrderList Works

**File**: `TradovateRestApiClient.cs`

Already implemented. Verify it returns correct data including ordStatus.

## Test Algorithm Flow

### New Test Mode: `full_lifecycle`

1. **Place limit order** (far from market so it won't fill)
2. **Wait 2 seconds** for order to propagate
3. **Read open orders** via `Transactions.GetOrders()` - verify our order is there
4. **Wait for Submitted event** - should come from PlaceOrder
5. **Cancel the order**
6. **Wait for Cancelled event** - should come from WebSocket
7. **Read open orders again** - verify order is gone
8. **Log summary** - success/failure of each step

### Test Parameters
- `test_mode`: "full_lifecycle"
- `contract_symbol`: "YMZ5" (current contract)
- `reference_price`: 44000 (current ~market)
- `offset_ticks`: 200 (place order $200 away, won't fill)

## Files to Modify

1. **TradovateWebSocketClient.cs** - Add SubscribeUserSync, OrderEventReceived event, parse order events
2. **TradovateBrokerage.cs** - Subscribe on connect, handle order events, fire QC OrderEvent
3. **TradovateRestApiClient.cs** - No changes needed (GetOrderList already works)
4. **TradovateOrderTest.cs** - Add full_lifecycle test mode

## Version Bump

Update `BrokerageVersion` constant to verify correct DLL:
```csharp
private const string BrokerageVersion = "2024-12-14-v4-order-lifecycle";
```

## Testing Steps

1. Build with `--no-incremental`
2. Run Docker test with `test_mode: "full_lifecycle"`
3. Verify logs show:
   - Version string on startup
   - Order placed with broker ID
   - WebSocket subscription sent
   - Order event received (Submitted)
   - Cancel request sent
   - Order event received (Cancelled)
   - Final order list is empty

## Risk Mitigations

1. **WebSocket disconnects** - Events might be missed. For production, need reconnect logic and REST polling fallback.
2. **Event ordering** - Events might arrive out of order. Log timestamps and handle gracefully.
3. **Missed events** - If test fails, manually check Tradovate UI and cancel any orphan orders.
