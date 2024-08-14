using System;
using CryptoToolkit;
using NGigGossip4Nostr;
using ProtoBuf;

namespace GigGossipFrames;

[ProtoContract]
public class BroadcastTopicResponse : IProtoFrame
{
    [ProtoMember(1)]
    public required Certificate<RequestPayloadValue> SignedRequestPayload { get; set; }
    [ProtoMember(2)]
    public required Certificate<CancelRequestPayloadValue> SignedCancelRequestPayload { get; set; }
}

