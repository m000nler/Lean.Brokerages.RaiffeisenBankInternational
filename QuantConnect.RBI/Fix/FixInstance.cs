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

using QuantConnect.RBI.Fix.Core.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Util;
using QuickFix;
using QuickFix.Fields;
using QuickFix.FIX42;
using QuickFix.Transport;
using Log = QuantConnect.Logging.Log;
using Message = QuickFix.Message;

namespace QuantConnect.RBI.Fix;

public class FixInstance : MessageCracker, IApplication, IDisposable
{
    private readonly IFixMessageHandler _messageHandler;
    private readonly FixConfiguration _config;
    private SocketInitiator _initiator;
    private readonly LogFactory.LogFactory _logFactory;
    private readonly OnBehalfOfCompID _onBehalfOfCompID;
    private readonly SecurityExchangeHours _securityExchangeHours;
    
    private readonly ManualResetEvent _loginEvent = new (false);
    private CancellationTokenSource _cancellationTokenSource;
    private volatile bool _connected;

    private bool _isDisposed;

    public EventHandler<FixError> Error;

    public FixInstance(IFixMessageHandler messageHandler, FixConfiguration config, bool logFixMesssages)
    {
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
        _config = config;
        _logFactory = new LogFactory.LogFactory(logFixMesssages);
        _onBehalfOfCompID = new OnBehalfOfCompID(config.OnBehalfOfCompID);
        _securityExchangeHours = MarketHoursDatabase.FromDataFolder().GetExchangeHours(Market.USA, null, SecurityType.Equity);
    }
    
    public bool IsConnected()
    {
        return _connected  && !_isDisposed;
    }

    private bool IsExchangeOpen(bool extendedMarketHours)
    {
        return _securityExchangeHours.IsOpen(DateTime.UtcNow.ConvertFromUtc(_securityExchangeHours.TimeZone),
            extendedMarketHours);
    }

    public void Initialize()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _connected = TryConnect();
        Task.Factory.StartNew(() =>
        {
            Log.Trace("FixInstance(): starting fix connection...");
            var retry = 0;
            var timeoutLoop = TimeSpan.FromMinutes(1);
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                if (_cancellationTokenSource.Token.WaitHandle.WaitOne(timeoutLoop))
                {
                    // exit time
                    break;
                }
                if (!TryConnect())
                {
                    Log.Error($"FixInstance(): connection failed");
                    if (++retry >= 5)
                    {
                        // after retrying to connect for X times & exchange is open we should die
                        Error?.Invoke(this, new FixError { Message = "Fix connection failed" });
                    }
                }
                else
                {
                    retry = 0;
                }
            }
            Log.Trace($"FixInstance(): ending connection monitor");
        });
    }

    public void Terminate()
    {
        _connected = false;
        // stop fix connection monitor
        _cancellationTokenSource.Cancel();
        if (_initiator != null && !_initiator.IsStopped)
        {
            _initiator.Stop();
        }
    }

    /// <summary>
    /// All outbound admin level messages pass through this callback.
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="sessionID">SessionID</param>
    public void ToAdmin(Message message, SessionID sessionID)
    {
        message.Header.SetField(_onBehalfOfCompID);
        _messageHandler.EnrichMessage(message);
    }

    /// <summary>
    /// Every inbound admin level message will pass through this method, such as heartbeats, logons, and logouts.
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="sessionID">SessionID</param>
    public void FromAdmin(Message message, SessionID sessionID)
    {
        _messageHandler.HandleAdminMessage(message, sessionID);
    }

    /// <summary>
    /// All outbound application level messages pass through this callback before they are sent. 
    /// If a tag needs to be added to every outgoing message, this is a good place to do that.
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="sessionID">SessionID</param>
    public void ToApp(Message message, SessionID sessionID)
    {
        message.Header.SetField(_onBehalfOfCompID);
    }

    /// <summary>
    /// Every inbound application level message will pass through this method, such as orders, executions, security definitions, and market data
    /// </summary>
    /// <param name="message"></param>
    /// <param name="sessionID"></param>
    public void FromApp(Message message, SessionID sessionID)
    {
        try
        {
            _messageHandler.Handle(message, sessionID);
        }
        catch (UnsupportedMessageType e)
        {
            Log.Error(e, $"[{sessionID}] Unknown message: {message.GetType().Name}: {message}");
        }
    }

    /// <summary>
    /// This method is called whenever a new session is created.
    /// </summary>
    /// <param name="sessionID"></param>
    public void OnCreate(SessionID sessionID)
    {
        Log.Trace($"FixInstance.Session created: {sessionID}");
    }

    /// <summary>
    /// Notifies when a successful logon has completed.
    /// </summary>
    /// <param name="sessionID">SessionID</param>
    public void OnLogout(SessionID sessionID)
    {
        _messageHandler.OnLogout(sessionID);
        _loginEvent.Set();
    }

    /// <summary>
    /// Notifies when a session is offline - either from an exchange of logout messages or network connectivity loss.
    /// </summary>
    /// <param name="sessionID">SessionID</param>
    public void OnLogon(SessionID sessionID)
    {
        _messageHandler.OnLogon(sessionID);
        _loginEvent.Set();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _initiator.DisposeSafely();
        _cancellationTokenSource.DisposeSafely();
    }

    public void OnMessage(ExecutionReport report)
    {
        _messageHandler.OnMessage(report, null);
    }

    public void OnMessage(OrderCancelReject reject)
    {
        _messageHandler.OnMessage(reject, null);
    }

    private bool TryConnect()
    {
        try
        {
            _config.Reset();

            // while the exchange is open and we are not connected, let's try to connect
            if (!_messageHandler.AreSessionsReady() && IsExchangeOpen(extendedMarketHours: true))
            {
                var count = 0;
                do
                {
                    _initiator.DisposeSafely();
                    _loginEvent.Reset();
                    var settings = _config.GetDefaultSessionSettings();
                    var sessionId = settings.GetSessions().Single();
                    
                    Log.Trace($"FixInstance.TryConnect({sessionId}): start...");

                    var storeFactory = new FileStoreFactory(settings);
                    _initiator = new SocketInitiator(this, storeFactory, settings, _logFactory,
                        _messageHandler.MessageFactory);
                    _initiator.Start();

                    if (!_loginEvent.WaitOne(TimeSpan.FromSeconds(10), _cancellationTokenSource.Token))
                    {
                        Log.Error($"FixInstance.TryConnect({sessionId}): Timeout initializing FIX session.");
                    }
                    else if (_messageHandler.AreSessionsReady())
                    {
                        Log.Trace($"FixInstance.TryConnect({sessionId}): Connected FIX session.");
                        return true;
                    }
                } while (!_cancellationTokenSource.IsCancellationRequested && ++count <= 15);

                return false;
            }

            // we are already connected or exchange is closed
            return true;
        }
        catch (Exception e)
        {
            Log.Error(e);
        }

        // something failed
        return false;
    }
}