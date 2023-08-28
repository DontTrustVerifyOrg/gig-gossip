using System;
using System.Diagnostics;
using NBitcoin.Secp256k1;
using NNostr.Client;
using CryptoToolkit;
using System.Threading;
using NBitcoin.RPC;

namespace NGigGossip4Nostr;


public abstract class NostrNode
{
    CompositeNostrClient nostrClient;
    protected ECPrivKey privateKey;
    protected ECXOnlyPubKey publicKey;
    public string PublicKey;
    private int chunkSize;

    public NostrNode(ECPrivKey privateKey, string[] nostrRelays,int chunkSize)
    {
        this.privateKey = privateKey;
        this.publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this.publicKey.AsHex();
        nostrClient = new CompositeNostrClient((from rel in nostrRelays select new System.Uri(rel)).ToArray());
        this.chunkSize = chunkSize;
    }

    class Message
    {
        public required string SenderPublicKey;
        public required object Frame;
    }

    protected void PublishContactList(Dictionary<string,NostrContact> contactList)
    {
        List<NostrEventTag> tags;
        lock (contactList)
        {
            tags = (from c in contactList.Values select new NostrEventTag() { TagIdentifier = "p", Data = { c.ContactPublicKey, c.Relay, c.Petname } }).ToList();
        }
        var newEvent = new NostrEvent()
        {
            Kind = 3,
            Content = "",
            Tags = tags,
        };
        newEvent.ComputeIdAndSignAsync(this.privateKey, handlenip4: false).Wait();
        nostrClient.SendEventsAndWaitUntilReceived(new[] { newEvent }, CancellationToken.None).Wait();
    }

    public string SendMessage(string targetPublicKey, object frame)
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
                Kind = 4,
                Content = part,
                Tags = {
                    new NostrEventTag(){ TagIdentifier ="p", Data = { targetPublicKey } },
                    new NostrEventTag(){ TagIdentifier ="x", Data = { evid } },
                    new NostrEventTag(){ TagIdentifier ="t", Data = {  frame.GetType().Name } },
                    new NostrEventTag(){ TagIdentifier ="i", Data = { idx.ToString() } },
                    new NostrEventTag(){ TagIdentifier ="n", Data = { numOfParts.ToString() } }
                }
            };

            newEvent.EncryptNip04EventAsync(this.privateKey).AsTask().Wait();
            newEvent.ComputeIdAndSignAsync(this.privateKey, handlenip4: false).Wait();
            events.Add(newEvent);
        }
        nostrClient.SendEventsAndWaitUntilReceived(events.ToArray(), CancellationToken.None).Wait();
        return evid;
    }

    public abstract void OnMessage(string eventId, string senderPublicKey, object frame);
    public abstract void OnContactList(string eventId, Dictionary<string, NostrContact> contactList);

    Thread mainThread;
    CancellationTokenSource subscribeForEventsTokenSource = new CancellationTokenSource();

    protected void Start()
    {
        nostrClient.ConnectAndWaitUntilConnected().Wait();
        mainThread = new Thread(async () =>
        {

            var q = nostrClient.SubscribeForEvents(new[]{
                new NostrSubscriptionFilter()
                {
                    Kinds = new []{3},
                    Authors = new []{ publicKey.ToHex() },
                },
                new NostrSubscriptionFilter()
                {
                    Kinds = new []{4},
                    ReferencedPublicKeys = new []{ publicKey.ToHex() }
                }
            }, false, subscribeForEventsTokenSource.Token);

            try
            { 
            await foreach (var nostrEvent in q)
                if (nostrEvent.Kind == 3)
                    ProcessContactList(nostrEvent);
                else
                    ProcessNewMessage(nostrEvent);
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
            }

        });
        mainThread.Start();
    }

    public virtual void Stop()
    {
        subscribeForEventsTokenSource.Cancel();
        mainThread.Join();
    }



    private Dictionary<string, SortedDictionary<int, string>> _partial_messages = new();

    private void ProcessNewMessage(NostrEvent nostrEvent)
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
                var msg =  nostrEvent.DecryptNip04EventAsync(this.privateKey).Result;
                if (partNum == 1)
                {
                    var type = tagDic["t"][0];
                    var t = System.Reflection.Assembly.Load("GigGossipFrames").GetType("NGigGossip4Nostr." + type);
                    var frame = Crypto.DeserializeObject(Convert.FromBase64String(msg),t);
                    this.OnMessage(idx, nostrEvent.PublicKey, frame);
                }
                else
                {
                    object frame = null;
                    lock (_partial_messages)
                    {
                        if (!_partial_messages.ContainsKey(idx))
                            _partial_messages[idx] = new SortedDictionary<int, string>();
                        _partial_messages[idx][parti] = msg;
                        if (_partial_messages[idx].Count == partNum)
                        {
                            var txt = string.Join("", _partial_messages[idx].Values);
                            var type = tagDic["t"][0];
                            var t = System.Reflection.Assembly.Load("GigGossipFrames").GetType("NGigGossip4Nostr." + type);
                            frame = Crypto.DeserializeObject(Convert.FromBase64String(txt), t);
                            _partial_messages.Remove(idx);
                        }
                    }
                    if (frame != null)
                        this.OnMessage(idx, nostrEvent.PublicKey, frame);
                }
            }
    }

    private void ProcessContactList(NostrEvent nostrEvent)
    {
        var newCL = new Dictionary<string, NostrContact>();
        foreach (var tag in nostrEvent.Tags)
        {
            if (tag.TagIdentifier == "p")
                newCL[tag.Data[0]] = new NostrContact() { PublicKey = tag.Data[0], Relay = tag.Data[1], Petname = tag.Data[2] };
        }
        OnContactList(nostrEvent.Id, newCL);
    }

}
