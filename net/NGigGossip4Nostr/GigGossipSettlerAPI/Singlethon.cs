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


public static class Singlethon
{
    public static Settler Settler = null;
    public static HubDicStore<GigReplCert> SymmetricKeys4UserPublicKey = new();
    public static HubDicStore<string> Preimages4UserPublicKey = new();
    public static ConcurrentDictionary<string, AsyncComQueue<SymmetricKeyRevealEventArgs>> SymmetricKeyAsyncComQueue4ConnectionId = new();
    public static ConcurrentDictionary<string, AsyncComQueue<PreimageRevealEventArgs>> PreimagesAsyncComQueue4ConnectionId = new();
}

