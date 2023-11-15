using System;
using CryptoToolkit;
using NGigGossip4Nostr;

namespace GigGossipFrames;

[Serializable]
public class BroadcastTopicResponse
{
	public required Certificate<RequestPayloadValue> SignedRequestPayload;
    public required Certificate<CancelRequestPayloadValue> SignedCancelRequestPayload;
}

