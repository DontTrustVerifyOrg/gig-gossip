using Invoicesrpc;
using Lnrpc;
using Routerrpc;
using CryptoToolkit;
using Grpc.Net.Client;
using Grpc.Core;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http;

namespace LNDClient;

public static class LND
{
    public class NodeSettings
    {
        public required string MacaroonFile { get; set; }
        public required string TlsCertFile { get; set; }
        public required string RpcHost { get; set; }
        public required string ListenHost { get; set; }
    }

    public static HttpClient GetSSLHttpClient(string tlsCertFile)
    {
        var httpClientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        var clientCert = new X509Certificate2(tlsCertFile);
        httpClientHandler.ClientCertificates.Add(clientCert);

        return new HttpClient(httpClientHandler);
    }

    static Invoicesrpc.Invoices.InvoicesClient InvoicesClient(NodeSettings conf)
    {
        return Client<Invoicesrpc.Invoices.InvoicesClient>(conf);
    }

    static Routerrpc.Router.RouterClient RouterClient(NodeSettings conf)
    {
        return Client<Routerrpc.Router.RouterClient>(conf);
    }

    static Lnrpc.Lightning.LightningClient LightningClient(NodeSettings conf)
    {
        return Client<Lnrpc.Lightning.LightningClient>(conf);
    }

    static Walletrpc.WalletKit.WalletKitClient WalletKit(NodeSettings conf)
    {
        return Client<Walletrpc.WalletKit.WalletKitClient>(conf);
    }


    static T Client<T>(NodeSettings conf) where T : ClientBase<T>
    {
        var channel = GrpcChannel.ForAddress(conf.RpcHost,
            new GrpcChannelOptions {
                HttpClient = GetSSLHttpClient(conf.TlsCertFile.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))) });

        var ctors = typeof(T).GetConstructors();
        var ctor = ctors.First(x => x.GetParameters().Length == 1 && x.GetParameters().Single().Name == "channel");
        return ctor.Invoke(new object[] { channel }) as T;
    }


    static SslCredentials GetSslCredentials(NodeSettings conf)
    {
        Environment.SetEnvironmentVariable("GRPC_SSL_CIPHER_SUITES", "HIGH+ECDSA");
        var cert = System.IO.File.ReadAllText(conf.TlsCertFile.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        var sslCreds = new SslCredentials(cert);
        return sslCreds;
    }

    static string GetMacaroon(NodeSettings conf)
    {
        return File.ReadAllBytes(conf.MacaroonFile.Replace("$HOME",Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))).AsHex();
    }

    static Metadata Metadata(NodeSettings conf)
    {
        return new Metadata() { new Metadata.Entry("macaroon", GetMacaroon(conf)) };
    }

    public static WalletBalanceResponse WalletBallance(NodeSettings conf)
    {
        return LightningClient(conf).WalletBalance(new WalletBalanceRequest() { },
            Metadata(conf));
    }

    public static string NewAddress(NodeSettings conf, string account = null)
    {
        var nar = new NewAddressRequest() { Type = AddressType.NestedPubkeyHash };
        if (account != null)
            nar.Account = account;
        var response = LightningClient(conf).NewAddress(nar,
            Metadata(conf));
        return response.Address;
    }

    //-1 means send all
    public static string SendCoins(NodeSettings conf, string address, string memo, long satoshis = -1, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var req = new SendCoinsRequest() { Addr = address, TargetConf = 6, Label = memo };
        if (satoshis > -1)
            req.Amount = satoshis;
        else
            req.SendAll = true;

        var response = LightningClient(conf).SendCoins(req, Metadata(conf), deadline, cancellationToken);
        return response.Txid;
    }


    public static AddInvoiceResponse AddInvoice(NodeSettings conf, long satoshis, string memo, long expiry = 86400, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).AddInvoice(
            new Invoice()
            {
                Memo = memo,
                Value = satoshis,
                Expiry = expiry,
            },
            Metadata(conf), deadline, cancellationToken);
    }

    public static Invoice LookupInvoice(NodeSettings conf, byte[] hash, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).LookupInvoice(
            new PaymentHash()
            {
                RHash = Google.Protobuf.ByteString.CopyFrom(hash)
            },
            Metadata(conf), deadline, cancellationToken);
    }

    public static PayReq DecodeInvoice(NodeSettings conf, string paymentRequest, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).DecodePayReq(
            new PayReqString()
            {
                PayReq = paymentRequest
            },
            Metadata(conf), deadline, cancellationToken);
    }

    public static SendResponse SendPayment(NodeSettings conf, string paymentRequest, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).SendPaymentSync(
            new SendRequest()
            {
                PaymentRequest = paymentRequest
            },
            Metadata(conf), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<Payment> SendPaymentV2(NodeSettings conf, string paymentRequest, int timeout, long feelimit, ulong[] outChanIds = null, DateTime? deadline = null,CancellationToken cancellationToken = default)
    {
        var spr = new SendPaymentRequest()
        {
            PaymentRequest = paymentRequest,
            TimeoutSeconds = timeout,
            FeeLimitSat = feelimit,
        };
        if(outChanIds!=null)
            spr.OutgoingChanIds.AddRange(outChanIds);

        var stream = RouterClient(conf).SendPaymentV2(
            spr,
            Metadata(conf), deadline, cancellationToken);
        return stream;
    }


    public static GetInfoResponse GetNodeInfo(NodeSettings conf, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).GetInfo(
            new GetInfoRequest(),
            Metadata(conf), deadline, cancellationToken);
    }

    public static void Connect(NodeSettings conf, string host, string nodepubkey, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        LightningClient(conf).ConnectPeer(
            new ConnectPeerRequest()
            {
                Addr = new LightningAddress() { Host = host, Pubkey = nodepubkey }
            },
            Metadata(conf), deadline, cancellationToken);
    }

    public static ListPeersResponse ListPeers(NodeSettings conf, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).ListPeers(
            new ListPeersRequest(),
            Metadata(conf), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<OpenStatusUpdate> OpenChannel(NodeSettings conf, string nodePubKey, long fundingSatoshis, string closeAddress = null, string memo = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var ocr = new OpenChannelRequest()
        {
            NodePubkey = Google.Protobuf.ByteString.CopyFrom(nodePubKey.AsBytes()),
            LocalFundingAmount = fundingSatoshis
        };
        if (closeAddress != null)
            ocr.CloseAddress = closeAddress;
        if (memo != null)
            ocr.Memo = memo;
        return LightningClient(conf).OpenChannel(ocr, Metadata(conf), deadline, cancellationToken);
    }

    public static BatchOpenChannelResponse BatchOpenChannel(NodeSettings conf,  List<(string,long)> amountsPerNode,DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var ocr = new BatchOpenChannelRequest()
        {
        };
        foreach (var (node,fnd) in amountsPerNode)
            ocr.Channels.Add(new Lnrpc.BatchOpenChannel() { NodePubkey = Google.Protobuf.ByteString.CopyFrom(node.AsBytes()), LocalFundingAmount = fnd });

        return LightningClient(conf).BatchOpenChannel(ocr, Metadata(conf), deadline, cancellationToken);
    }

    public static ChannelPoint OpenChannelSync(NodeSettings conf, string nodePubKey, long fundingSatoshis, string closeAddress = null, string memo = null, bool privat = false, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var ocr = new OpenChannelRequest()
        {
            NodePubkeyString = nodePubKey,
            Private = privat,
        };
        if (fundingSatoshis <= 0)
        {
            ocr.FundMax = true;
        }
        else
        {
            ocr.LocalFundingAmount = fundingSatoshis;
        }
        if (closeAddress != null)
            ocr.CloseAddress = closeAddress;
        if (memo != null)
            ocr.Memo = memo;
        return LightningClient(conf).OpenChannelSync(ocr, Metadata(conf), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<CloseStatusUpdate> CloseChannel(NodeSettings conf, string channelpoint, string closeAddress = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var cp = channelpoint.Split(':');
        var ccr = new CloseChannelRequest()
        {
            ChannelPoint = new ChannelPoint() { FundingTxidStr = cp[0], OutputIndex = uint.Parse(cp[1]) }
        };
        if (closeAddress != null)
            ccr.DeliveryAddress = closeAddress;
        var stream = LightningClient(conf).CloseChannel(
            ccr,
            Metadata(conf), deadline, cancellationToken);
        return stream;
    }

    public static PendingChannelsResponse PendingChannels(NodeSettings conf, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).PendingChannels(
            new PendingChannelsRequest() { },
            Metadata(conf), deadline, cancellationToken);
    }

    public static ListChannelsResponse ListChannels(NodeSettings conf, bool activeOnly = true, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).ListChannels(
            new ListChannelsRequest()
            {
                ActiveOnly = activeOnly, 
            },
            Metadata(conf), deadline, cancellationToken);
    }

    public static AddHoldInvoiceResp AddHodlInvoice(NodeSettings conf, long satoshis, string memo, byte[] hash, long expiry = 86400, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var ahr = new AddHoldInvoiceRequest()
        {
            Memo = memo,
            Value = satoshis,
            Hash = Google.Protobuf.ByteString.CopyFrom(hash),
            Expiry = expiry,
        };

        return InvoicesClient(conf).AddHoldInvoice(
            ahr,
            Metadata(conf), deadline, cancellationToken);
    }

    public static void CancelInvoice(NodeSettings conf, byte[] hash, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        InvoicesClient(conf).CancelInvoice(
            new CancelInvoiceMsg()
            {
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
            },
            Metadata(conf), deadline, cancellationToken);
    }

    public static void SettleInvoice(NodeSettings conf, byte[] preimage, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        InvoicesClient(conf).SettleInvoice(
            new SettleInvoiceMsg()
            {
                Preimage = Google.Protobuf.ByteString.CopyFrom(preimage)
            },
            Metadata(conf), deadline, cancellationToken);
    }



    public static TransactionDetails GetTransactions(NodeSettings conf, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).GetTransactions(
            new GetTransactionsRequest()
            { },
            Metadata(conf), deadline, cancellationToken);
    }

    public static Walletrpc.RequiredReserveResponse RequiredReserve(NodeSettings conf, uint additionalChannelsNum,  DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var lur = new Walletrpc.RequiredReserveRequest()
        {  AdditionalPublicChannels = additionalChannelsNum };
        return WalletKit(conf).RequiredReserve(lur,
                    Metadata(conf), deadline, cancellationToken);
    }

    public static Walletrpc.ListUnspentResponse ListUnspent(NodeSettings conf, int minConfs, string account = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var lur = new Walletrpc.ListUnspentRequest()
        { MinConfs = minConfs };
        if (account != null)
            lur.Account = account;
        return WalletKit(conf).ListUnspent(lur,
            Metadata(conf), deadline, cancellationToken);
    }

    public static Invoice LookupInvoiceV2(NodeSettings conf, byte[] hash, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return InvoicesClient(conf).LookupInvoiceV2(
            new LookupInvoiceMsg()
            {
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash)
            },
            Metadata(conf), deadline, cancellationToken);
    }

    public static ListInvoiceResponse ListInvoices(NodeSettings conf, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).ListInvoices(
            new ListInvoiceRequest() { },
            Metadata(conf), deadline, cancellationToken);
    }

    public static ListPaymentsResponse ListPayments(NodeSettings conf, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf).ListPayments(
            new ListPaymentsRequest() { },
            Metadata(conf), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<Payment> TrackPayments(NodeSettings conf, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return RouterClient(conf).TrackPayments(
            new TrackPaymentsRequest() { },
            Metadata(conf), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<Invoice> SubscribeSingleInvoice(NodeSettings conf, byte[] hash, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var stream = InvoicesClient(conf).SubscribeSingleInvoice(
            new SubscribeSingleInvoiceRequest()
            {
                RHash = Google.Protobuf.ByteString.CopyFrom(hash)

            }, Metadata(conf), deadline, cancellationToken);

        return stream;
    }

    public static AsyncServerStreamingCall<Invoice> SubscribeInvoices(NodeSettings conf, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var stream = LightningClient(conf).SubscribeInvoices(
            new InvoiceSubscription()
            {
            }, Metadata(conf), deadline, cancellationToken);

        return stream;
    }

}

