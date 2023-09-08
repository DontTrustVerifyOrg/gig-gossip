using System;
using LNDWallet;

namespace GigLNDWalletAPI;

public static class Singlethon
{
    public static LNDWalletManager LNDWalletManager = null;
    public static Dictionary<string, HashSet<string>> InvoiceHashes4UserPublicKey = new();
    public static Dictionary<string, HashSet<string>> PaymentHashes4UserPublicKey = new();
}

