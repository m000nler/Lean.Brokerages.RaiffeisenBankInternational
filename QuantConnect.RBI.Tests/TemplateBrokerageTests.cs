﻿/*
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

using NUnit.Framework;
using QuantConnect.Configuration;
using QuantConnect.Tests;
using QuantConnect.Interfaces;
using QuantConnect.RBI.Fix;
using QuantConnect.RBI.Fix.Core.Implementations;
using QuantConnect.Securities;
using QuantConnect.Tests.Brokerages;
using QuickFix;

namespace QuantConnect.RBI.Tests
{
    [TestFixture]
    public partial class TemplateBrokerageTests : BrokerageTests
    {
        private readonly FixConfiguration _fixConfiguration = new FixConfiguration
        {
            SenderCompId = Config.Get("wex-sender-comp-id"),
            TargetCompId = Config.Get("wex-target-comp-id"),
            Host = Config.Get("wex-host"),
            Port = Config.Get("wex-port")
        };


        /// <summary>
        /// Provides the data required to test each order type in various cases
        /// </summary>
        private static TestCaseData[] OrderParameters()
        {
            return new[]
            {
                new TestCaseData(new MarketOrderTestParameters(Symbols.BTCUSD)).SetName("MarketOrder"),
                new TestCaseData(new LimitOrderTestParameters(Symbols.BTCUSD, 10000m, 0.01m)).SetName("LimitOrder"),
                new TestCaseData(new StopMarketOrderTestParameters(Symbols.BTCUSD, 10000m, 0.01m)).SetName("StopMarketOrder"),
                new TestCaseData(new StopLimitOrderTestParameters(Symbols.BTCUSD, 10000m, 0.01m)).SetName("StopLimitOrder"),
                new TestCaseData(new LimitIfTouchedOrderTestParameters(Symbols.BTCUSD, 10000m, 0.01m)).SetName("LimitIfTouchedOrder")
            };
        }
        
        [Test]
        public void LogOn()
        {
            var controller = new FixBrokerageController();
            var messageHandler = new FixMessageHandler(_fixConfiguration, controller);

            using var fixInstance = new FixInstance(messageHandler, _fixConfiguration);

            fixInstance.Initialize();

            var sessionId = new SessionID(_fixConfiguration.FixVersionString, _fixConfiguration.SenderCompId, _fixConfiguration.TargetCompId);

            fixInstance.OnLogon(sessionId);
        }

        protected override IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider)
        {
            throw new System.NotImplementedException();
        }

        protected override Symbol Symbol { get; }
        protected override SecurityType SecurityType { get; }
        protected override bool IsAsync()
        {
            throw new System.NotImplementedException();
        }

        protected override decimal GetAskPrice(Symbol symbol)
        {
            throw new System.NotImplementedException();
        }
    }
}