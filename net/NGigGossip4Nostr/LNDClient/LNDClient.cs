using Grpc.Core;
using Invoicesrpc;
using Lnrpc;
using NBitcoin;
using Routerrpc;
using CryptoToolkit;

namespace LNDClient;

public static class LND
{
    public interface IMacaroon
    {
        public string GetMacaroon();
    }

    public class MacaroonFile : IMacaroon
    {
        string macaroonPath;

        public MacaroonFile(string macaroonPath)
        {
            this.macaroonPath = macaroonPath;
        }

        public string GetMacaroon()
        {
            return File.ReadAllBytes(macaroonPath).AsHex();
        }
    }

    public class MacaroonString : IMacaroon
    {
        byte[] macaroonBytes;

        public MacaroonString(byte[] macaroonBytes)
        {
            this.macaroonBytes = macaroonBytes;
        }

        public string GetMacaroon()
        {
            return macaroonBytes.AsHex();
        }
    }

    public class NodesConfiguration
    {
        private List<IMacaroon> macaroons = new();
        private List<string> tlsCertPath = new();
        private List<string> rpcHost = new();
        private List<string> nodeListenOn = new();

        public int AddNodeConfiguration(IMacaroon macaroon, string tlsCertPath, string rpcHost, string nodeListenOn)
        {
            this.macaroons.Add(macaroon);
            this.tlsCertPath.Add(tlsCertPath);
            this.rpcHost.Add(rpcHost);
            this.nodeListenOn.Add(nodeListenOn);
            return this.macaroons.Count;
        }

        public NodesConfiguration ForMacaroon(IMacaroon macaroon, int idx)
        {
            var ret = new NodesConfiguration();
            ret.AddNodeConfiguration(macaroon, TlsCert(idx), RpcHost(idx), ListenHost(idx));
            return ret;
        }

        public string ListenHost(int idx)
        {
            return nodeListenOn[idx - 1];
        }

        public string RpcHost(int idx)
        {
            return rpcHost[idx - 1];
        }

        public string TlsCert(int idx)
        {
            return tlsCertPath[idx - 1];
        }
        public IMacaroon Macaroon(int idx)
        {
            return macaroons[idx - 1];
        }
    }


    static Invoicesrpc.Invoices.InvoicesClient InvoicesClient(NodesConfiguration conf, int idx)
    {
        var channel = new Grpc.Core.Channel(conf.RpcHost(idx), GetSslCredentials(conf, idx));
        var client = new Invoicesrpc.Invoices.InvoicesClient(channel);
        return client;
    }

    static Routerrpc.Router.RouterClient RouterClient(NodesConfiguration conf, int idx)
    {
        var channel = new Grpc.Core.Channel(conf.RpcHost(idx), GetSslCredentials(conf, idx));
        var client = new Routerrpc.Router.RouterClient(channel);
        return client;
    }

    static Lnrpc.Lightning.LightningClient LightningClient(NodesConfiguration conf, int idx)
    {
        var channel = new Grpc.Core.Channel(conf.RpcHost(idx), GetSslCredentials(conf, idx));
        var client = new Lnrpc.Lightning.LightningClient(channel);
        return client;
    }

    static Walletrpc.WalletKit.WalletKitClient WalletKit(NodesConfiguration conf, int idx)
    {
        var channel = new Grpc.Core.Channel(conf.RpcHost(idx), GetSslCredentials(conf, idx));
        var client = new Walletrpc.WalletKit.WalletKitClient(channel);
        return client;
    }


    static SslCredentials GetSslCredentials(NodesConfiguration conf, int idx)
    {
        Environment.SetEnvironmentVariable("GRPC_SSL_CIPHER_SUITES", "HIGH+ECDSA");
        var cert = System.IO.File.ReadAllText(conf.TlsCert(idx));
        var sslCreds = new SslCredentials(cert);
        return sslCreds;
    }

    static string GetMacaroon(NodesConfiguration conf, int idx)
    {
        return conf.Macaroon(idx).GetMacaroon();
    }

    static Metadata Metadata(NodesConfiguration conf, int idx)
    {
        return new Metadata() { new Metadata.Entry("macaroon", GetMacaroon(conf, idx)) };
    }

    public static string NewAddress(NodesConfiguration conf, int idx, string account = null)
    {
        var nar = new NewAddressRequest() { Type = AddressType.NestedPubkeyHash };
        if (account != null)
            nar.Account = account;
        var response = LightningClient(conf, idx).NewAddress(nar,
            Metadata(conf, idx));
        return response.Address;
    }

    //-1 means send all
    public static string SendCoins(NodesConfiguration conf, int idx, string address, string memo, long satoshis = -1, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var req = new SendCoinsRequest() { Addr = address, TargetConf = 6, Label = memo };
        if (satoshis > -1)
            req.Amount = satoshis;
        else
            req.SendAll = true;

        var response = LightningClient(conf, idx).SendCoins(req, Metadata(conf, idx), deadline, cancellationToken);
        return response.Txid;
    }


    public static AddInvoiceResponse AddInvoice(NodesConfiguration conf, int idx, long satoshis, string memo, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).AddInvoice(
            new Invoice()
            {
                Memo = memo,
                Value = satoshis,
            },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static Invoice LookupInvoice(NodesConfiguration conf, int idx, byte[] hash, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).LookupInvoice(
            new PaymentHash()
            {
                RHash = Google.Protobuf.ByteString.CopyFrom(hash)
            },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static PayReq DecodeInvoice(NodesConfiguration conf, int idx, string paymentRequest, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).DecodePayReq(
            new PayReqString()
            {
                PayReq = paymentRequest
            },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static SendResponse SendPayment(NodesConfiguration conf, int idx, string paymentRequest, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).SendPaymentSync(
            new SendRequest()
            {
                PaymentRequest = paymentRequest
            },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<Payment> SendPaymentV2(NodesConfiguration conf, int idx, string paymentRequest, int timeout, long feelimit, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var spr = new SendPaymentRequest()
        {
            PaymentRequest = paymentRequest,
            TimeoutSeconds = timeout,
            FeeLimitSat = feelimit,
        };

        var stream = RouterClient(conf, idx).SendPaymentV2(
            spr,
            Metadata(conf, idx), deadline, cancellationToken);
        return stream;
    }


    public static GetInfoResponse GetNodeInfo(NodesConfiguration conf, int idx, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).GetInfo(
            new GetInfoRequest(),
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static void Connect(NodesConfiguration conf, int idx, string host, string nodepubkey, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        LightningClient(conf, idx).ConnectPeer(
            new ConnectPeerRequest()
            {
                Addr = new LightningAddress() { Host = host, Pubkey = nodepubkey }
            },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static ListPeersResponse ListPeers(NodesConfiguration conf, int idx, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).ListPeers(
            new ListPeersRequest(),
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<OpenStatusUpdate> OpenChannel(NodesConfiguration conf, int idx, string nodePubKey, long fundingSatoshis, string closeAddress = null, string memo = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var ocr = new OpenChannelRequest()
        {
            LocalFundingAmount = fundingSatoshis,
            NodePubkeyString = nodePubKey,
        };
        if (closeAddress != null)
            ocr.CloseAddress = closeAddress;
        if (memo != null)
            ocr.Memo = memo;
        return LightningClient(conf, idx).OpenChannel(ocr, Metadata(conf, idx), deadline, cancellationToken);
    }


    public static ChannelPoint OpenChannelSync(NodesConfiguration conf, int idx, string nodePubKey, long fundingSatoshis, string closeAddress = null, string memo = null, bool privat = false, DateTime? deadline = null, CancellationToken cancellationToken = default)
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
        return LightningClient(conf, idx).OpenChannelSync(ocr, Metadata(conf, idx), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<CloseStatusUpdate> CloseChannel(NodesConfiguration conf, int idx, string channelpoint, string closeAddress = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var cp = channelpoint.Split(':');
        var ccr = new CloseChannelRequest()
        {
            ChannelPoint = new ChannelPoint() { FundingTxidStr = cp[0], OutputIndex = uint.Parse(cp[1]) }
        };
        if (closeAddress != null)
            ccr.DeliveryAddress = closeAddress;
        var stream = LightningClient(conf, idx).CloseChannel(
            ccr,
            Metadata(conf, idx), deadline, cancellationToken);
        return stream;
    }

    public static PendingChannelsResponse PendingChannels(NodesConfiguration conf, int idx, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).PendingChannels(
            new PendingChannelsRequest() { },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static ListChannelsResponse ListChannels(NodesConfiguration conf, int idx, bool activeOnly = true, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).ListChannels(
            new ListChannelsRequest()
            {
                ActiveOnly = activeOnly
            },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static AddHoldInvoiceResp AddHodlInvoice(NodesConfiguration conf, int idx, long satoshis, string memo, byte[] hash, long expiry = 86400, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var ahr = new AddHoldInvoiceRequest()
        {
            Memo = memo,
            Value = satoshis,
            Hash = Google.Protobuf.ByteString.CopyFrom(hash),
            Expiry = expiry,
        };

        return InvoicesClient(conf, idx).AddHoldInvoice(
            ahr,
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static void CancelInvoice(NodesConfiguration conf, int idx, byte[] hash, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        InvoicesClient(conf, idx).CancelInvoice(
            new CancelInvoiceMsg()
            {
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
            },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static void SettleInvoice(NodesConfiguration conf, int idx, byte[] preimage, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        InvoicesClient(conf, idx).SettleInvoice(
            new SettleInvoiceMsg()
            {
                Preimage = Google.Protobuf.ByteString.CopyFrom(preimage)
            },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static byte[] ComputePaymentHash(byte[] preimage)
    {
        Span<byte> buf = stackalloc byte[32];
        var sha256 = System.Security.Cryptography.SHA256.Create();
        sha256.TryComputeHash(preimage, buf, out _);
        return buf.ToArray();
    }

    public static byte[] GenerateRandomPreimage()
    {
        return RandomUtils.GetBytes(32);
    }

    public static TransactionDetails GetTransactions(NodesConfiguration conf, int idx, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).GetTransactions(
            new GetTransactionsRequest()
            { },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static Walletrpc.ListUnspentResponse ListUnspent(NodesConfiguration conf, int idx, int minConfs, string account = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var lur = new Walletrpc.ListUnspentRequest()
        { MinConfs = minConfs };
        if (account != null)
            lur.Account = account;
        return WalletKit(conf, idx).ListUnspent(lur,
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static Invoice LookupInvoiceV2(NodesConfiguration conf, int idx, byte[] hash, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return InvoicesClient(conf, idx).LookupInvoiceV2(
            new LookupInvoiceMsg()
            {
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash)
            },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static ListInvoiceResponse ListInvoices(NodesConfiguration conf, int idx, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).ListInvoices(
            new ListInvoiceRequest() { },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static ListPaymentsResponse ListPayments(NodesConfiguration conf, int idx, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return LightningClient(conf, idx).ListPayments(
            new ListPaymentsRequest() { },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<Payment> TrackPayments(NodesConfiguration conf, int idx, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        return RouterClient(conf, idx).TrackPayments(
            new TrackPaymentsRequest() { },
            Metadata(conf, idx), deadline, cancellationToken);
    }

    public static AsyncServerStreamingCall<Invoice> SubscribeSingleInvoice(NodesConfiguration conf, int idx, byte[] hash, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var stream = InvoicesClient(conf, idx).SubscribeSingleInvoice(
            new SubscribeSingleInvoiceRequest()
            {
                RHash = Google.Protobuf.ByteString.CopyFrom(hash)

            }, Metadata(conf, idx), deadline, cancellationToken);

        return stream;
    }

    public static AsyncServerStreamingCall<Invoice> SubscribeInvoices(NodesConfiguration conf, int idx, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var stream = LightningClient(conf, idx).SubscribeInvoices(
            new InvoiceSubscription()
            {
            }, Metadata(conf, idx), deadline, cancellationToken);

        return stream;
    }
}

