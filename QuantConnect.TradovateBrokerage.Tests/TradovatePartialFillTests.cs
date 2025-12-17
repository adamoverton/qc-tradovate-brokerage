using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Collections.Generic;
using QuantConnect.Brokerages.Tradovate.Api;

namespace QuantConnect.Brokerages.Tradovate.Tests
{
    /// <summary>
    /// Tests for partial fill handling in Tradovate brokerage.
    ///
    /// Key insight: Tradovate sends cumulative FilledQty in order updates,
    /// but LEAN expects incremental fill quantities per OrderEvent.
    /// The brokerage must track previous cumulative fills and compute deltas.
    /// </summary>
    [TestFixture]
    public class TradovatePartialFillTests
    {
        #region Fill Calculation Logic Tests

        /// <summary>
        /// Helper class to test fill calculation logic in isolation
        /// </summary>
        private class FillCalculator
        {
            private readonly Dictionary<long, int> _cumulativeFills = new Dictionary<long, int>();

            /// <summary>
            /// Calculate incremental fill from cumulative update
            /// </summary>
            /// <param name="orderId">Broker order ID</param>
            /// <param name="cumulativeFilledQty">Total filled quantity (cumulative)</param>
            /// <returns>Incremental fill quantity for this event</returns>
            public int CalculateIncrementalFill(long orderId, int cumulativeFilledQty)
            {
                var previousCumulative = _cumulativeFills.GetValueOrDefault(orderId, 0);
                var incremental = cumulativeFilledQty - previousCumulative;

                if (cumulativeFilledQty > 0)
                {
                    _cumulativeFills[orderId] = cumulativeFilledQty;
                }

                return incremental;
            }

            /// <summary>
            /// Clean up tracking for an order (called when order is terminal)
            /// </summary>
            public void RemoveOrder(long orderId)
            {
                _cumulativeFills.Remove(orderId);
            }

            /// <summary>
            /// Get the tracked cumulative fill for an order
            /// </summary>
            public int GetTrackedCumulative(long orderId)
            {
                return _cumulativeFills.GetValueOrDefault(orderId, 0);
            }
        }

        [Test]
        public void CalculateIncrementalFill_FirstFill_ReturnsFullQuantity()
        {
            var calculator = new FillCalculator();

            // First fill of 5 contracts
            var incremental = calculator.CalculateIncrementalFill(orderId: 12345, cumulativeFilledQty: 5);

            Assert.That(incremental, Is.EqualTo(5));
        }

        [Test]
        public void CalculateIncrementalFill_SecondFill_ReturnsDelta()
        {
            var calculator = new FillCalculator();

            // First fill: 5 contracts
            calculator.CalculateIncrementalFill(orderId: 12345, cumulativeFilledQty: 5);

            // Second fill: cumulative now 8 (so 3 more filled)
            var incremental = calculator.CalculateIncrementalFill(orderId: 12345, cumulativeFilledQty: 8);

            Assert.That(incremental, Is.EqualTo(3));
        }

        [Test]
        public void CalculateIncrementalFill_MultipleFills_ReturnsCorrectDeltas()
        {
            var calculator = new FillCalculator();
            long orderId = 12345;

            // Simulate order for 10 contracts filling in 4 parts
            var fill1 = calculator.CalculateIncrementalFill(orderId, cumulativeFilledQty: 2);
            var fill2 = calculator.CalculateIncrementalFill(orderId, cumulativeFilledQty: 5);
            var fill3 = calculator.CalculateIncrementalFill(orderId, cumulativeFilledQty: 7);
            var fill4 = calculator.CalculateIncrementalFill(orderId, cumulativeFilledQty: 10);

            Assert.That(fill1, Is.EqualTo(2), "First fill should be 2");
            Assert.That(fill2, Is.EqualTo(3), "Second fill should be 3 (5-2)");
            Assert.That(fill3, Is.EqualTo(2), "Third fill should be 2 (7-5)");
            Assert.That(fill4, Is.EqualTo(3), "Fourth fill should be 3 (10-7)");

            // Total should equal original order size
            Assert.That(fill1 + fill2 + fill3 + fill4, Is.EqualTo(10));
        }

        [Test]
        public void CalculateIncrementalFill_ZeroCumulative_ReturnsZero()
        {
            var calculator = new FillCalculator();

            // Status update with no fill (e.g., Working status)
            var incremental = calculator.CalculateIncrementalFill(orderId: 12345, cumulativeFilledQty: 0);

            Assert.That(incremental, Is.EqualTo(0));
        }

        [Test]
        public void CalculateIncrementalFill_SameCumulativeTwice_ReturnsZeroSecondTime()
        {
            var calculator = new FillCalculator();
            long orderId = 12345;

            // First update with fill
            var fill1 = calculator.CalculateIncrementalFill(orderId, cumulativeFilledQty: 5);

            // Second update with same cumulative (e.g., status change, no new fill)
            var fill2 = calculator.CalculateIncrementalFill(orderId, cumulativeFilledQty: 5);

            Assert.That(fill1, Is.EqualTo(5));
            Assert.That(fill2, Is.EqualTo(0), "Duplicate cumulative should return 0 incremental");
        }

        [Test]
        public void CalculateIncrementalFill_MultipleOrders_TrackedIndependently()
        {
            var calculator = new FillCalculator();

            // Two different orders filling concurrently
            var order1_fill1 = calculator.CalculateIncrementalFill(orderId: 111, cumulativeFilledQty: 3);
            var order2_fill1 = calculator.CalculateIncrementalFill(orderId: 222, cumulativeFilledQty: 5);
            var order1_fill2 = calculator.CalculateIncrementalFill(orderId: 111, cumulativeFilledQty: 7);
            var order2_fill2 = calculator.CalculateIncrementalFill(orderId: 222, cumulativeFilledQty: 10);

            Assert.That(order1_fill1, Is.EqualTo(3));
            Assert.That(order2_fill1, Is.EqualTo(5));
            Assert.That(order1_fill2, Is.EqualTo(4), "Order 1 second fill: 7-3=4");
            Assert.That(order2_fill2, Is.EqualTo(5), "Order 2 second fill: 10-5=5");
        }

        [Test]
        public void RemoveOrder_CleansUpTracking()
        {
            var calculator = new FillCalculator();
            long orderId = 12345;

            // Partial fill
            calculator.CalculateIncrementalFill(orderId, cumulativeFilledQty: 5);
            Assert.That(calculator.GetTrackedCumulative(orderId), Is.EqualTo(5));

            // Order cancelled - clean up
            calculator.RemoveOrder(orderId);
            Assert.That(calculator.GetTrackedCumulative(orderId), Is.EqualTo(0));

            // If same order ID reused (unlikely but possible), should start fresh
            var newFill = calculator.CalculateIncrementalFill(orderId, cumulativeFilledQty: 3);
            Assert.That(newFill, Is.EqualTo(3));
        }

        #endregion

        #region TradovateOrderUpdate Model Tests

        [Test]
        public void TradovateOrderUpdate_FilledQty_DefaultsToZero()
        {
            var update = new TradovateOrderUpdate();
            Assert.That(update.FilledQty, Is.EqualTo(0));
        }

        [Test]
        public void TradovateOrderUpdate_AllProperties_CanBeSet()
        {
            var update = new TradovateOrderUpdate
            {
                EventType = "Updated",
                EntityType = "order",
                OrderId = 12345678,
                AccountId = 9999,
                ContractId = 123,
                OrdStatus = "PartiallyFilled",
                Action = "Buy",
                OrderType = "Limit",
                Qty = 10,
                FilledQty = 5,
                Price = 44000m,
                StopPrice = null,
                AvgFillPrice = 43995.50m,
                Text = "Partial fill"
            };

            Assert.That(update.EventType, Is.EqualTo("Updated"));
            Assert.That(update.OrdStatus, Is.EqualTo("PartiallyFilled"));
            Assert.That(update.FilledQty, Is.EqualTo(5));
            Assert.That(update.AvgFillPrice, Is.EqualTo(43995.50m));
        }

        #endregion

        #region Edge Cases

        [Test]
        public void CalculateIncrementalFill_LargeOrderId_Works()
        {
            var calculator = new FillCalculator();

            // Tradovate uses long order IDs
            long largeOrderId = 4211558917L;

            var fill = calculator.CalculateIncrementalFill(largeOrderId, cumulativeFilledQty: 100);
            Assert.That(fill, Is.EqualTo(100));
        }

        [Test]
        public void CalculateIncrementalFill_SingleContractOrder_Works()
        {
            var calculator = new FillCalculator();

            // Order for 1 contract fills completely
            var fill = calculator.CalculateIncrementalFill(orderId: 12345, cumulativeFilledQty: 1);
            Assert.That(fill, Is.EqualTo(1));
        }

        [Test]
        public void CalculateIncrementalFill_LargeQuantity_Works()
        {
            var calculator = new FillCalculator();

            // Large institutional order
            var fill1 = calculator.CalculateIncrementalFill(orderId: 12345, cumulativeFilledQty: 500);
            var fill2 = calculator.CalculateIncrementalFill(orderId: 12345, cumulativeFilledQty: 1000);

            Assert.That(fill1, Is.EqualTo(500));
            Assert.That(fill2, Is.EqualTo(500));
        }

        #endregion
    }
}
