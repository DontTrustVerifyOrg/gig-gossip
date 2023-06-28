using System;
namespace NGigGossip4Nostr;

public class RequestPayload : SignableObject
{
    public Guid PayloadId { get; set; }
    public AbstractTopic Topic { get; set; }
    public Certificate SenderCertificate { get; set; }
}