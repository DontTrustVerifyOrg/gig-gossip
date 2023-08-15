using System;
using System.Diagnostics;
using NBitcoin.Secp256k1;
using NNostr.Client;
using CryptoToolkit;
using System.Threading;

namespace NGigGossip4Nostr;

public class NostrContact
{
    public string PublicKey;
    public string Relay;
    public string Petname;
}

public abstract class NostrNode
{
    CompositeNostrClient nostrClient;
    protected ECPrivKey _privateKey;
    protected ECXOnlyPubKey _publicKey;
    public string PublicKey;
    private Dictionary<string, NostrContact> _contactList;
    public NostrNode(ECPrivKey privateKey, string[] nostrRelays)
    {
        this._privateKey = privateKey;
        this._publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this._publicKey.AsHex();
        nostrClient = new CompositeNostrClient((from rel in nostrRelays select new System.Uri(rel)).ToArray());
        this._contactList = new();
    }

    class Message
    {
        public string SenderPublicKey;
        public object Frame;
    }

    const int CHUNK_SIZE = 2048;

    public async void AddContact(NostrContact newContact)
    {
        if (newContact.PublicKey == this.PublicKey)
            throw new Exception("Cannot connect node to itself");
        List<NostrEventTag> tags;
        lock (_contactList)
        {
            _contactList[newContact.PublicKey] = newContact;
            tags = (from c in _contactList.Values select new NostrEventTag() { TagIdentifier = "p", Data = { c.PublicKey, c.Relay, c.Petname } }).ToList();
        }
        var newEvent = new NostrEvent()
        {
            Kind = 3,
            Content = "",
            Tags = tags, 
        };
        await newEvent.ComputeIdAndSignAsync(this._privateKey, handlenip4: false);
        await nostrClient.SendEventsAndWaitUntilReceived(new[] { newEvent }, CancellationToken.None);
    }

    public async void SendMessage(string targetPublicKey, object frame)
    {
        var message = Convert.ToBase64String(Crypto.SerializeObject(frame));
        var evid = Guid.NewGuid().ToString();

        int numOfParts = 1 + message.Length / CHUNK_SIZE;
        List<NostrEvent> events = new();
        for (int idx = 0; idx < numOfParts; idx++)
        {
            var part = ((idx + 1) * CHUNK_SIZE < message.Length) ? message.Substring(idx * CHUNK_SIZE, CHUNK_SIZE) : message.Substring(idx * CHUNK_SIZE);
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

            await newEvent.EncryptNip04EventAsync(this._privateKey);
            await newEvent.ComputeIdAndSignAsync(this._privateKey, handlenip4: false);
            events.Add(newEvent);
        }
        await nostrClient.SendEventsAndWaitUntilReceived(events.ToArray(), CancellationToken.None);
    }

    public abstract void OnMessage(string senderPublicKey, object frame);

    Thread mainThread;

    public virtual void Start()
    {
        nostrClient.ConnectAndWaitUntilConnected().Wait();
        mainThread = new Thread(async () =>
        {

            var q = nostrClient.SubscribeForEvents(new[]{
                new NostrSubscriptionFilter()
                {
                    Kinds = new []{3},
                    Authors = new []{ _publicKey.ToHex() },
                },
                new NostrSubscriptionFilter()
                {
                    Kinds = new []{4},
                    ReferencedPublicKeys = new []{ _publicKey.ToHex() }
                }
            }, false, CancellationToken.None);

            await foreach (var nostrEvent in q)
                if (nostrEvent.Kind == 3)
                    ProcessContactList(nostrEvent);
                else
                    ProcessNewMessage(nostrEvent);

        });
        mainThread.Start();
    }

    private Dictionary<string, SortedDictionary<int, string>> _partial_messages = new();

    private async void ProcessNewMessage(NostrEvent nostrEvent)
    {
        Dictionary<string, List<string>> tagDic = new();
        foreach (var tag in nostrEvent.Tags)
        {
            if (tag.Data.Count > 0)
                tagDic[tag.TagIdentifier] = tag.Data;
        }
        if (tagDic.ContainsKey("p") && tagDic.ContainsKey("t"))
            if (tagDic["p"][0] == _publicKey.ToHex())
            {
                int parti = int.Parse(tagDic["i"][0]);
                int partNum = int.Parse(tagDic["n"][0]);
                string idx = tagDic["x"][0];
                var msg = await nostrEvent.DecryptNip04EventAsync(this._privateKey);
                if (partNum == 1)
                {
                    var type = tagDic["t"][0];
                    var t = System.Reflection.Assembly.Load("GigGossipFrames").GetType("NGigGossip4Nostr." + type);
                    var frame = Crypto.DeserializeObject(Convert.FromBase64String(msg),t);
                    this.OnMessage(nostrEvent.PublicKey, frame);
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
                        this.OnMessage(nostrEvent.PublicKey, frame);
                }
            }
    }

    private void ProcessContactList(NostrEvent nostrEvent)
    {
        lock (_contactList)
        {
            var newCL = new Dictionary<string, NostrContact>();
            foreach (var tag in nostrEvent.Tags)
            {
                if (tag.TagIdentifier == "p")
                    newCL[tag.Data[0]] = new NostrContact() { PublicKey = tag.Data[0], Relay = tag.Data[1], Petname = tag.Data[2] };
            }
            _contactList = newCL;
        }
    }

    public List<string> GetContacts()
    {
        lock (_contactList)
        {
            return _contactList.Keys.ToList();
        }
    }
}
