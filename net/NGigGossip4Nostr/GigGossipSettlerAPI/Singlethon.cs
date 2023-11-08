using System;
using GigGossipSettler;

namespace GigGossipSettlerAPI;

public static class Singlethon
{
    public static Settler Settler = null;
    public static HubDicStore<Tuple<Guid,Guid>> SymmetricKeys4UserPublicKey = new();
    public static HubDicStore<string> Preimages4UserPublicKey = new();
}

