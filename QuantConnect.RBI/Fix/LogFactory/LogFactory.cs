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

using System.Collections.Concurrent;
using QuickFix;

namespace QuantConnect.RBI.Fix.LogFactory;

public class LogFactory : ILogFactory
{
    private static readonly ConcurrentDictionary<SessionID, ILog> Loggers = new();
    private readonly bool _logFixMessages;

    public LogFactory(bool logFixMesssages)
    {
        _logFixMessages = logFixMesssages;
    }
    
    public ILog Create(SessionID sessionID)
    {
        return Loggers.GetOrAdd(sessionID, new Logger(_logFixMessages));
    }
}