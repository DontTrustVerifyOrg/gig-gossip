using System;
using Grpc.Core;

namespace LITClient;

public static class LIT
{
    public class NodesConfiguration
    {
        private List<string> macaroonPath = new();
        private List<string> tlsCertPath = new();
        private List<string> rpcHost = new();
        public int AddNodeConfiguration(string macaroonPath, string tlsCertPath, string rpcHost)
        {
            this.macaroonPath.Add(macaroonPath);
            this.tlsCertPath.Add(tlsCertPath);
            this.rpcHost.Add(rpcHost);
            return this.macaroonPath.Count;
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

    static Litrpc.Accounts.AccountsClient AccountsClient(NodesConfiguration conf, int idx)
    {
        var channel = new Grpc.Core.Channel(conf.RpcHost(idx), GetSslCredentials(conf, idx));
        var client = new Litrpc.Accounts.AccountsClient(channel);
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

    public static Litrpc.CreateAccountResponse CreateAccount(NodesConfiguration conf, int idx, ulong accountBalance, string label)
    {
        return AccountsClient(conf, idx).CreateAccount(
            new Litrpc.CreateAccountRequest() { AccountBalance = accountBalance, Label = label },
            Metadata(conf, idx));
    }

    public static Litrpc.ListAccountsResponse ListAccounts(NodesConfiguration conf, int idx)
    {
        return AccountsClient(conf, idx).ListAccounts(
            new Litrpc.ListAccountsRequest() { },
            Metadata(conf, idx));
    }

    public static Litrpc.RemoveAccountResponse RemoveAccount(NodesConfiguration conf, int idx, string label)
    {
        return AccountsClient(conf, idx).RemoveAccount(
            new Litrpc.RemoveAccountRequest() { Label = label },
            Metadata(conf, idx));
    }

    public static Litrpc.Account AccountInfo(NodesConfiguration conf, int idx, string label)
    {
        return AccountsClient(conf, idx).AccountInfo(
            new Litrpc.AccountInfoRequest() { Label = label },
            Metadata(conf, idx));
    }

    public static Litrpc.Account UpdateAccount(NodesConfiguration conf, int idx, string label, long accountBalance)
    {
        return AccountsClient(conf, idx).UpdateAccount(
            new Litrpc.UpdateAccountRequest() { Label = label, AccountBalance = accountBalance },
            Metadata(conf, idx));
    }
}