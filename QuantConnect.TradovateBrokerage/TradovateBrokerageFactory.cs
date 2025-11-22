/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using QuantConnect.Packets;
using QuantConnect.Brokerages;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Configuration;
using QuantConnect.Util;
using QuantConnect.Data;
using QuantConnect.Brokerages.Tradovate.Api;

namespace QuantConnect.Brokerages.Tradovate
{
    /// <summary>
    /// Provides factory for Tradovate brokerage
    /// </summary>
    public class TradovateBrokerageFactory : BrokerageFactory
    {
        /// <summary>
        /// Gets the brokerage data required to run the brokerage from configuration/disk
        /// </summary>
        /// <remarks>
        /// The implementation of this property will create the brokerage data dictionary required for
        /// running live jobs. See <see cref="IJobQueueHandler.NextJob"/>
        /// </remarks>
        public override Dictionary<string, string> BrokerageData { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TradovateBrokerageFactory"/> class
        /// </summary>
        public TradovateBrokerageFactory() : base(typeof(TradovateBrokerage))
        {
        }

        /// <summary>
        /// Gets a brokerage model that can be used to model this brokerage's unique behaviors
        /// </summary>
        /// <param name="orderProvider">The order provider</param>
        public override IBrokerageModel GetBrokerageModel(IOrderProvider orderProvider)
        {
            return new DefaultBrokerageModel();
        }

        /// <summary>
        /// Creates a new IBrokerage instance
        /// </summary>
        /// <param name="job">The job packet to create the brokerage for</param>
        /// <param name="algorithm">The algorithm instance</param>
        /// <returns>A new brokerage instance</returns>
        public override IBrokerage CreateBrokerage(LiveNodePacket job, IAlgorithm algorithm)
        {
            var username = Config.Get("tradovate-username");
            var password = Config.Get("tradovate-password");
            var clientId = Config.Get("tradovate-client-id");
            var clientSecret = Config.Get("tradovate-client-secret");
            var environment = Config.Get("environment", "tradovate-demo");

            var tradovateEnvironment = environment.Contains("live")
                ? TradovateEnvironment.Live
                : TradovateEnvironment.Demo;

            var aggregator = Composer.Instance.GetPart<IDataAggregator>();

            return new TradovateBrokerage(aggregator, username, password, clientId, clientSecret, tradovateEnvironment);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            // Nothing to dispose
        }
    }
}