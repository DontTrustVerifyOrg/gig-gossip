using System;
using System.Collections.Concurrent;
using LNDWallet;
using NetworkToolkit;

namespace GigLNDWalletAPI;

public static class Singlethon
{
    public static LNDWalletManager LNDWalletManager = null;

    public static HubDicStore<string> PaymentHashes4PublicKey = new();
    public static HubDicStore<string> InvoiceHashes4PublicKey = new();
    public static ConcurrentDictionary<string, AsyncComQueue<PaymentStatusChangedEventArgs>> PaymentAsyncComQueue4ConnectionId = new();
    public static ConcurrentDictionary<string, AsyncComQueue<InvoiceStateChangedEventArgs>> InvoiceAsyncComQueue4ConnectionId = new();
}

