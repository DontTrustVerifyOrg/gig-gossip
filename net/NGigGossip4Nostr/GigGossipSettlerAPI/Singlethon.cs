using System;
using GigGossipSettler;

namespace GigGossipSettlerAPI;
public struct GigReplCert
{
    public required Guid GigId;
    public required Guid ReplierCertificateId;
}


public static class Singlethon
{
    public static Settler Settler = null;
    public static HubDicStore<GigReplCert> SymmetricKeys4UserPublicKey = new();
    public static HubDicStore<string> Preimages4UserPublicKey = new();
}

