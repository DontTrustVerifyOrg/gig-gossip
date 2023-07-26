using Grpc.Core;
using Invoicesrpc;
using Lnrpc;
using NBitcoin;
using Routerrpc;

using System.Text.Json;
using static LNDClient.LND;

namespace LNDClient;

public static class LND
{
    public class NodesConfiguration
    {
        private List<string> macaroonPath = new();
        private List<string> tlsCertPath = new();
        private List<string> rpcHost = new();
        private List<string> nodeListenOn = new();
        public int AddNodeConfiguration(string macaroonPath, string tlsCertPath, string rpcHost, string nodeListenOn)
        {
            this.macaroonPath.Add(macaroonPath);
            this.tlsCertPath.Add(tlsCertPath);
            this.rpcHost.Add(rpcHost);
            this.nodeListenOn.Add(nodeListenOn);
            return this.macaroonPath.Count;
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
        public string MacaroonPath(int idx)
        {
            return macaroonPath[idx - 1];
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

    static Lnrpc.Lightning.LightningClient UserClient(NodesConfiguration conf, int idx)
    {
        var channel = new Grpc.Core.Channel(conf.RpcHost(idx), GetSslCredentials(conf, idx));
        var client = new Lnrpc.Lightning.LightningClient(channel);
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
        byte[] macaroonBytes = File.ReadAllBytes(conf.MacaroonPath(idx));
        var macaroon = BitConverter.ToString(macaroonBytes).Replace("-", "");
        // hex format stripped of "-" chars
        return macaroon;
    }

    static Metadata Metadata(NodesConfiguration conf, int idx)
    {
        return new Metadata() { new Metadata.Entry("macaroon", GetMacaroon(conf, idx)) };
    }

    public static long GetWalletBalance(NodesConfiguration conf, int idx)
    {
        var response = UserClient(conf, idx).WalletBalance(
            new WalletBalanceRequest(),
            Metadata(conf, idx));
        return response.TotalBalance;
    }

    public static string AddInvoice(NodesConfiguration conf, int idx, long satoshis, string memo)
    {
        var invoiceResponse = UserClient(conf, idx).AddInvoice(
            new Invoice()
            {
                Memo = memo,
                Value = satoshis
            },
            Metadata(conf, idx));
        return invoiceResponse.PaymentRequest;
    }

    public static string LookupInvoice(NodesConfiguration conf, int idx, byte[] hash)
    {
        var invoiceResponse = UserClient(conf, idx).LookupInvoice(
            new PaymentHash()
            {
                RHash = Google.Protobuf.ByteString.CopyFrom(hash)
            },
            Metadata(conf, idx));
        return invoiceResponse.State.ToString();
    }

    public static string DecodeInvoice(NodesConfiguration conf, int idx, string paymentRequest)
    {
        var invoiceResponse = UserClient(conf, idx).DecodePayReq(
            new PayReqString()
            {
                PayReq = paymentRequest
            },
            Metadata(conf, idx));
        return JsonSerializer.Serialize(invoiceResponse);
    }

    public static AsyncUnaryCall<SendResponse> SendPayment(NodesConfiguration conf, int idx, string paymentRequest)
    {
        var response = UserClient(conf, idx).SendPaymentSyncAsync(
            new SendRequest()
            {
                PaymentRequest = paymentRequest,
            },
            Metadata(conf, idx));
        return response;
    }


    public static AsyncServerStreamingCall<Payment> SendPaymentV2(NodesConfiguration conf, int idx, string paymentRequest, int timeout)
    {
        var stream = RouterClient(conf, idx).SendPaymentV2(
            new SendPaymentRequest()
            {
                PaymentRequest = paymentRequest,
                TimeoutSeconds = timeout,
            },
            Metadata(conf, idx));

        return stream;
    }

    public static async Task<Tuple<string?, Task<bool>?>> AwaitForSendPaymentV2Return(AsyncServerStreamingCall<Payment> stream, double timeout, Task<bool> task)
    {
        var delay = Task.Delay(TimeSpan.FromSeconds(timeout));
        if (task == null)
            task = stream.ResponseStream.MoveNext();
        var winner = await Task.WhenAny(task, delay);
        if (winner == delay)
            return Tuple.Create<string?, Task<bool>?>(null, task);
        var ret = task.Result ? stream.ResponseStream.Current.Status.ToString() : "eof";
        return Tuple.Create<string?, Task<bool>?>(ret, null);
    }

    public static string GetNodePubkey(NodesConfiguration conf, int idx)
    {
        var response = UserClient(conf, idx).GetInfo(
            new GetInfoRequest(),
            Metadata(conf, idx));
        return response.IdentityPubkey;
    }

    public static void Connect(NodesConfiguration conf, int idx, int idx2, string node2PubKey)
    {
        var response = UserClient(conf, idx).ConnectPeer(
            new ConnectPeerRequest()
            {
                Addr = new LightningAddress() { Host = conf.ListenHost(idx2), Pubkey = node2PubKey }
            },
            Metadata(conf, idx));
    }

    public static Dictionary<string, string> ListPeers(NodesConfiguration conf, int idx)
    {
        var response = UserClient(conf, idx).ListPeers(
            new ListPeersRequest(),
            Metadata(conf, idx));
        return new Dictionary<string, string>(from peer in response.Peers select KeyValuePair.Create(peer.PubKey, peer.Address));
    }

    public static string OpenChannel(NodesConfiguration conf, int idx, string nodePubKey, long fundingSatoshis)
    {
        var response = UserClient(conf, idx).OpenChannelSync(
            new OpenChannelRequest()
            {
                LocalFundingAmount = fundingSatoshis,
                NodePubkeyString = nodePubKey
            },
            Metadata(conf, idx));
        return BitConverter.ToString(response.FundingTxidBytes.ToByteArray().Reverse().ToArray()).Replace("-", "").ToLower();
    }

    public static void CloseChannel(NodesConfiguration conf, int idx, string fundingTxidStr)
    {
        var response = UserClient(conf, idx).CloseChannel(
            new CloseChannelRequest()
            {
                ChannelPoint = new ChannelPoint() { FundingTxidStr = fundingTxidStr }
            },
            Metadata(conf, idx));
    }

    static Dictionary<string, Dictionary<string, long>> ChannelsToC(IEnumerable<Lnrpc.Channel> channels)
    {
        var ret = new Dictionary<string, Dictionary<string, long>>();
        foreach (var channel in channels)
        {
            if (!ret.ContainsKey(channel.RemotePubkey))
                ret[channel.RemotePubkey] = new Dictionary<string, long>();
            ret[channel.RemotePubkey][channel.ChannelPoint.Split(':')[0]] = channel.LocalBalance;
        }
        return ret;
    }

    static Dictionary<string, Dictionary<string, long>> ChannelsToPO(IEnumerable<PendingChannelsResponse.Types.PendingChannel> channels)
    {
        var ret = new Dictionary<string, Dictionary<string, long>>();
        foreach (var channel in channels)
        {
            var cp = channel.ChannelPoint.Split(':')[0];
            if (!ret.ContainsKey(cp))
                ret[cp] = new Dictionary<string, long>();
            ret[cp][channel.RemoteNodePub] = channel.LocalBalance;
        }
        return ret;
    }


    public static Dictionary<string, Dictionary<string, Dictionary<string, long>>> PendingChannels(NodesConfiguration conf, int idx)
    {
        var response = UserClient(conf, idx).PendingChannels(
            new PendingChannelsRequest() { },
            Metadata(conf, idx));
        var po = ChannelsToPO(from channel in response.PendingOpenChannels select channel.Channel);
        var pc = ChannelsToPO(from channel in response.WaitingCloseChannels select channel.Channel);
        var pfc = ChannelsToPO(from channel in response.PendingForceClosingChannels select channel.Channel);
        var ret = new Dictionary<string, Dictionary<string, Dictionary<string, long>>>();
        ret["open"] = po;
        ret["waitingclose"] = pc;
        ret["forceclosing"] = pfc;
        return ret;
    }

    public static Dictionary<string, Dictionary<string, long>> ListChannels(NodesConfiguration conf, int idx, bool activeOnly = true)
    {
        var response = UserClient(conf, idx).ListChannels(
            new ListChannelsRequest()
            {
                ActiveOnly = activeOnly
            },
            Metadata(conf, idx));
        return ChannelsToC(response.Channels);
    }



    public static string AddHodlInvoice(NodesConfiguration conf, int idx, long satoshis, string memo, byte[] hash, long expiry = 86400)
    {
        var invoiceResponse = InvoicesClient(conf, idx).AddHoldInvoice(
            new AddHoldInvoiceRequest()
            {
                Memo = memo,
                Value = satoshis,
                Hash = Google.Protobuf.ByteString.CopyFrom(hash),
                Expiry = expiry
            },
            Metadata(conf, idx));
        return invoiceResponse.PaymentRequest;
    }

    public static void CancelInvoice(NodesConfiguration conf, int idx, byte[] hash)
    {
        var response = InvoicesClient(conf, idx).CancelInvoice(
            new CancelInvoiceMsg()
            {
                PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
            },
            Metadata(conf, idx));
    }

    public static void SettleInvoice(NodesConfiguration conf, int idx, byte[] preimage)
    {
        var response = InvoicesClient(conf, idx).SettleInvoice(
            new SettleInvoiceMsg()
            {
                Preimage = Google.Protobuf.ByteString.CopyFrom(preimage)
            },
            Metadata(conf, idx));
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

    public static string LookupInvoiceV2(NodesConfiguration conf, int idx, byte[] hash)
    {
        var lim = new LookupInvoiceMsg()
        {
            PaymentHash = Google.Protobuf.ByteString.CopyFrom(hash),
        };
        var invoiceResponse = InvoicesClient(conf, idx).LookupInvoiceV2(
            lim,
            Metadata(conf, idx));
        return invoiceResponse.State.ToString();
    }

    public static AsyncServerStreamingCall<Invoice> SubscribeSingleInvoice(NodesConfiguration conf, int idx, byte[] hash)
    {
        var stream = InvoicesClient(conf, idx).SubscribeSingleInvoice(
            new SubscribeSingleInvoiceRequest()
            {
                RHash = Google.Protobuf.ByteString.CopyFrom(hash),
            }, Metadata(conf, idx));

        return stream;
    }

    public static async Task<Tuple<string?, Task<bool>?>> AwaitForSubscribeSingleInvoiceReturn(AsyncServerStreamingCall<Invoice> stream, double timeout, Task<bool> task)
    {
        var delay = Task.Delay(TimeSpan.FromSeconds(timeout));
        if (task == null)
            task = stream.ResponseStream.MoveNext();
        var winner = await Task.WhenAny(task, delay);
        if (winner == delay)
            return Tuple.Create<string?, Task<bool>?>(null, task);
        var ret = task.Result ? stream.ResponseStream.Current.State.ToString() : "eof";
        return Tuple.Create<string?, Task<bool>?>(ret, null);
    }
}

