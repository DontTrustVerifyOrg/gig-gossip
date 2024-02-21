using System;
using System.Collections.Concurrent;
using GigGossipSettler;
using Nito.AsyncEx;

namespace GigGossipSettlerAPI;
public struct GigReplCert
{
    public required Guid SignerRequestPayloadId;
    public required Guid ReplierCertificateId;
}

public struct ChannelKey
{
    public required string PubKey;
    public required string Channel;
}

public struct ChannelVal
{
    public required string Code;
    public required int Retries;
}

public static class Singlethon
{
    public static Settler Settler = null;
    public static HubDicStore<GigReplCert> GigStatus4UserPublicKey = new();
    public static HubDicStore<string> Preimages4UserPublicKey = new();
    public static ConcurrentDictionary<string, AsyncComQueue<GigStatusEventArgs>> GigStatusAsyncComQueue4ConnectionId = new();
    public static ConcurrentDictionary<string, AsyncComQueue<PreimageRevealEventArgs>> PreimagesAsyncComQueue4ConnectionId = new();
    public static ConcurrentDictionary<ChannelKey, ChannelVal> channelSmsCodes = new();
}

