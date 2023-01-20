﻿using System;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.RBI.Fix.Connection.Interfaces;
using QuantConnect.RBI.Fix.Core.Interfaces;
using QuickFix.Fields;
using QuickFix.FIX42;

namespace QuantConnect.RBI.Fix.Core.Implementations;

public class FixSymbolController : IFixSymbolController
{
    private readonly IRBIFixConnection _session;
    private readonly RBISymbolMapper _symbolMapper;

    public FixSymbolController(IRBIFixConnection session)
    {
        _session = session;
        _symbolMapper = new RBISymbolMapper();
    }

    public bool SubscribeToSymbol(Symbol symbol)
    {
        throw new System.NotImplementedException();
    }

    public bool UnsubscribeFromSymbol(Symbol symbol)
    {
        throw new System.NotImplementedException();
    }
    
    public NewOrderSingle PlaceOrder(Order order)
    {
        var ticker = _symbolMapper.GetBrokerageSymbol(order.Symbol);

        var securityType = new QuickFix.Fields.SecurityType(_symbolMapper.GetBrokerageSecurityType(order.Symbol.SecurityType));

        var side = new Side(order.Direction == OrderDirection.Buy ? Side.BUY : Side.SELL);
        
        var newOrder = new NewOrderSingle()
        {
            ClOrdID = new ClOrdID(order.Id.ToString()),
            HandlInst = new HandlInst(HandlInst.AUTOMATED_EXECUTION_ORDER_PRIVATE_NO_BROKER_INTERVENTION),
            Symbol = new QuickFix.Fields.Symbol(ticker),
            Side = side,
            TransactTime = new TransactTime(DateTime.UtcNow),
            OrderQty = new OrderQty(order.Quantity),
            SecurityType = securityType
        };

        switch (order.Type)
        {
            case OrderType.Limit:
                newOrder.OrdType = new OrdType(OrdType.LIMIT);
                newOrder.Price = new Price(((LimitOrder) order).LimitPrice);
                break;
            
            case OrderType.Market:
                newOrder.OrdType = new OrdType(OrdType.MARKET);
                break;
            
            case OrderType.StopLimit:
                newOrder.OrdType = new OrdType(OrdType.STOP_LIMIT);
                newOrder.Price = new Price(((StopLimitOrder) order).LimitPrice);
                newOrder.StopPx = new StopPx(((StopLimitOrder) order).StopPrice);
                break;
            
            case OrderType.StopMarket:
                newOrder.OrdType = new OrdType(OrdType.STOP);
                newOrder.StopPx = new StopPx(((StopMarketOrder) order).StopPrice);
                break;
            
            // add market on limit
            default:
                Logging.Log.Trace($"RBI doesn't support this Order Type: {nameof(order.Type)}");
                break;
        }

        Log.Trace($"FixSymbolController.PlaceOrder(): sending order {order.Id}...");
        _session.Send(newOrder);
        return newOrder;
    }
}