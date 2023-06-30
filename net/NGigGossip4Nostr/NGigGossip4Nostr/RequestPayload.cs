using System;
namespace NGigGossip4Nostr;

[Serializable]
public class RequestPayload : SignableObject
{
    public Guid PayloadId { get; set; }
    public AbstractTopic Topic { get; set; }
    public Certificate SenderCertificate { get; set; }
}