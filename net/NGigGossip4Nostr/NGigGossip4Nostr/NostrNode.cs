using System;
using System.Diagnostics;
using NBitcoin.Secp256k1;
using NNostr.Client;
using CryptoToolkit;
using System.Threading;
using NBitcoin.RPC;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NGigGossip4Nostr;


public abstract class NostrNode
{
    const int HelloKind = 10127;
    const int ContactListKind = 3;
    const int RegularMessageKind = 4;
    const int EphemeralMessageKind = 20004;

    CompositeNostrClient nostrClient;
    protected ECPrivKey privateKey;
    protected ECXOnlyPubKey publicKey;
    public string PublicKey;
    private int chunkSize;
    public string[] NostrRelays { get; private set; }
    private Dictionary<string, Type> _registeredFrameTypes = new();
    private string subscriptionId;

    public NostrNode(ECPrivKey privateKey, int chunkSize)
    {
        this.privateKey = privateKey;
        this.publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this.publicKey.AsHex();
        this.chunkSize = chunkSize;
    }

    public NostrNode(NostrNode me, int chunkSize)
    {
        this.privateKey = me.privateKey;
        this.publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this.publicKey.AsHex();
        this.chunkSize = chunkSize;
    }

    public void RegisterFrameType<T>()
    {
        var t = typeof(T);
        _registeredFrameTypes[t.Name] = t;
    }

    private Type? GetFrameType(string name)
    {
        if (!_registeredFrameTypes.ContainsKey(name))
            return null;
        return _registeredFrameTypes[name];
    }
    class Message
    {
        public required string SenderPublicKey;
        public required object Frame;
    }

    protected async Task SayHelloAsync()
    {
        var newEvent = new NostrEvent()
        {
            Kind = HelloKind,
            Content = "",
            Tags = {
            },
        };
        await newEvent.ComputeIdAndSignAsync(this.privateKey, handlenip4: false);
        await nostrClient.PublishEvent(newEvent, CancellationToken.None);
    }

    protected async Task PublishContactListAsync(Dictionary<string, NostrContact> contactList)
    {
        List<NostrEventTag> tags;
        lock (contactList)
        {
            tags = (from c in contactList.Values select new NostrEventTag() { TagIdentifier = "p", Data = { c.ContactPublicKey, c.Relay, c.Petname } }).ToList();
        }
        var newEvent = new NostrEvent()
        {
            Kind = ContactListKind,
            Content = "",
            Tags = tags,
        };
        await newEvent.ComputeIdAndSignAsync(this.privateKey, handlenip4: false);
        await nostrClient.PublishEvent(newEvent, CancellationToken.None);
    }

    public async Task<string> SendMessageAsync(string targetPublicKey, object frame, bool ephemeral, DateTime? expiration = null)
    {
        var message = Convert.ToBase64String(Crypto.SerializeObject(frame));
        var evid = Guid.NewGuid().ToString();

        int numOfParts = 1 + message.Length / chunkSize;
        List<NostrEvent> events = new();
        for (int idx = 0; idx < numOfParts; idx++)
        {
            var part = ((idx + 1) * chunkSize < message.Length) ? message.Substring(idx * chunkSize, chunkSize) : message.Substring(idx * chunkSize);
            var newEvent = new NostrEvent()
            {
                Kind = ephemeral ? EphemeralMessageKind : RegularMessageKind,
                Content = part,
                Tags = {
                    new NostrEventTag(){ TagIdentifier ="p", Data = { targetPublicKey } },
                    new NostrEventTag(){ TagIdentifier ="x", Data = { evid } },
                    new NostrEventTag(){ TagIdentifier ="t", Data = {  frame.GetType().Name } },
                    new NostrEventTag(){ TagIdentifier ="i", Data = { idx.ToString() } },
                    new NostrEventTag(){ TagIdentifier ="n", Data = { numOfParts.ToString() } }
                }
            };
            if (expiration != null)
                newEvent.Tags.Add(
                    new NostrEventTag()
                    {
                        TagIdentifier = "expiration",
                        Data = { ((DateTimeOffset)expiration.Value).ToUnixTimeSeconds().ToString() }
                    });


            await newEvent.EncryptNip04EventAsync(this.privateKey, skipKindVerification: true);
            await newEvent.ComputeIdAndSignAsync(this.privateKey, handlenip4: false);
            events.Add(newEvent);
        }
        foreach (var e in events)
            await nostrClient.PublishEvent(e, CancellationToken.None);
        return evid;
    }

    public abstract Task OnMessageAsync(string eventId, string senderPublicKey, object frame);
    public abstract void OnContactList(string eventId, Dictionary<string, NostrContact> contactList);
    public abstract void OnHello(string eventId, string senderPublicKeye);

    protected async Task StartAsync(string[] nostrRelays)
    {
        if (nostrClient != null)
            await StopAsync();
        NostrRelays = nostrRelays;
        nostrClient = new CompositeNostrClient((from rel in nostrRelays select new System.Uri(rel)).ToArray());
        await nostrClient.ConnectAndWaitUntilConnected();
        nostrClient.EventsReceived += NostrClient_EventsReceived;
        subscriptionId = Guid.NewGuid().ToString();
        await nostrClient.CreateSubscription(subscriptionId, new[]{
                        new NostrSubscriptionFilter()
                        {
                            Kinds = new []{HelloKind},
                        },
                        new NostrSubscriptionFilter()
                        {
                            Kinds = new []{ContactListKind},
                            Authors = new []{ publicKey.ToHex() },
                        },
                        new NostrSubscriptionFilter()
                        {
                            Kinds = new []{RegularMessageKind},
                            ReferencedPublicKeys = new []{ publicKey.ToHex() }
                        },
                        new NostrSubscriptionFilter()
                        {
                            Kinds = new []{EphemeralMessageKind},
                            ReferencedPublicKeys = new []{ publicKey.ToHex() }
                        }
                    });
    }

    private async void NostrClient_EventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) e)
    {
        if (e.subscriptionId == subscriptionId)
        {
            foreach (var nostrEvent in e.events)
            {
                if (nostrEvent.Kind == HelloKind)
                    ProcessHello(nostrEvent);
                else if (nostrEvent.Kind == ContactListKind)
                    ProcessContactList(nostrEvent);
                else
                    await ProcessNewMessageAsync(nostrEvent);
            }
        }
    }

    public virtual async Task StopAsync()
    {
        await nostrClient.CloseSubscription(subscriptionId);
        await nostrClient.Disconnect();
        nostrClient.Dispose();
    }

    private ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _partial_messages = new();

    private async Task ProcessNewMessageAsync(NostrEvent nostrEvent)
    {
        Dictionary<string, List<string>> tagDic = new();
        foreach (var tag in nostrEvent.Tags)
        {
            if (tag.Data.Count > 0)
                tagDic[tag.TagIdentifier] = tag.Data;
        }
        if (tagDic.ContainsKey("p") && tagDic.ContainsKey("t"))
            if (tagDic["p"][0] == publicKey.ToHex())
            {
                int parti = int.Parse(tagDic["i"][0]);
                int partNum = int.Parse(tagDic["n"][0]);
                string idx = tagDic["x"][0];
                var msg = await nostrEvent.DecryptNip04EventAsync(this.privateKey, skipKindVerification: true);
                if (partNum == 1)
                {
                    var t = GetFrameType(tagDic["t"][0]);
                    if (t == null)
                    {
                        Trace.TraceWarning("Unrecognised Frame detected");
                        return;
                    }
                    var frame = Crypto.DeserializeObject(Convert.FromBase64String(msg), t);
                    await this.OnMessageAsync(idx, nostrEvent.PublicKey, frame);
                }
                else
                {
                    var inner_dic = _partial_messages.GetOrAdd(idx, (idx) => new ConcurrentDictionary<int, string>());
                    if (inner_dic.TryAdd(parti, msg))
                    {
                        if (inner_dic.Count == partNum)
                        {
                            _partial_messages.TryRemove(idx, out _);
                            var txt = string.Join("", new SortedDictionary<int, string>(inner_dic).Values);
                            var t = GetFrameType(tagDic["t"][0]);
                            if (t == null)
                            {
                                Trace.TraceWarning("Unrecognised Frame detected");
                                return;
                            }
                            var frame = Crypto.DeserializeObject(Convert.FromBase64String(txt), t);
                            await this.OnMessageAsync(idx, nostrEvent.PublicKey, frame);
                        }
                    }
                }
            }
    }

    private void ProcessContactList(NostrEvent nostrEvent)
    {
        var newCL = new Dictionary<string, NostrContact>();
        foreach (var tag in nostrEvent.Tags)
        {
            if (tag.TagIdentifier == "p")
                newCL[tag.Data[0]] = new NostrContact() { PublicKey = this.PublicKey, ContactPublicKey = tag.Data[0], Relay = tag.Data[1], Petname = tag.Data[2] };
        }
        OnContactList(nostrEvent.Id, newCL);
    }

    private void ProcessHello(NostrEvent nostrEvent)
    {
        OnHello(nostrEvent.Id, nostrEvent.PublicKey);
    }

}
