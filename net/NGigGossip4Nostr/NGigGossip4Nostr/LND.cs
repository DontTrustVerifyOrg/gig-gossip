using System;
namespace NGigGossip4Nostr;
public static class LND
{
    public static byte[] ComputePaymentHash(byte[] preimage)
    {
        return Crypto.ComputeSha512(new List<byte[]> { preimage });
    }
}

