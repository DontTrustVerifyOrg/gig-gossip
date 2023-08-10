using System;
using CryptoToolkit;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr;

[Serializable]
public class RequestPayload : SignableObject
{
    public Guid PayloadId { get; set; }
    public AbstractTopic Topic { get; set; }
    public Certificate SenderCertificate { get; set; }

    public new void Sign(ECPrivKey privateKey)
    {
        base.Sign(privateKey);
    }

    public new bool Verify(ECXOnlyPubKey publicKey)
    {
        return base.Verify(publicKey);
    }

}