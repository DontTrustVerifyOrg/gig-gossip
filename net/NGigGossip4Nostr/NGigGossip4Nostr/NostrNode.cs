using System;
using System.Diagnostics;
using NBitcoin.Secp256k1;
using NNostr.Client;
using CryptoToolkit;

namespace NGigGossip4Nostr;

public abstract class NostrNode
{
    CompositeNostrClient nostrClient;
    protected ECPrivKey _privateKey;
    protected ECXOnlyPubKey _publicKey;
    public string Name;
    public NostrNode(ECPrivKey privateKey, string[] nostrRelays)
    {
        this._privateKey = privateKey;
        this._publicKey = privateKey.CreateXOnlyPubKey();
        this.Name = this._publicKey.AsHex();
        nostrClient = new CompositeNostrClient((from rel in nostrRelays select new System.Uri(rel)).ToArray());
        nostrClient.EventsReceived += NostrClient_EventsReceived;
    }

    class Message
    {
        public string senderNodeName;
        public object frame;
    }

    const int CHUNK_SIZE = 2048;

    public async void SendMessage(string targetNodeName, object frame)
    {
        var message = Convert.ToBase64String(Crypto.SerializeObject(frame));
        var evid = Guid.NewGuid().ToString();

        int numOfParts = 1+message.Length / CHUNK_SIZE;
        for (int idx = 0; idx < numOfParts; idx ++)
        {
            var part = ((idx+1)* CHUNK_SIZE < message.Length)? message.Substring(idx* CHUNK_SIZE, CHUNK_SIZE) :message.Substring(idx* CHUNK_SIZE);
            var newEvent = new NostrEvent()
            {
                Kind = 4,
                Content = part,
                Tags = {
                    new NostrEventTag() { TagIdentifier = "p", Data = { targetNodeName } } ,
                    new NostrEventTag(){TagIdentifier ="x",Data = {evid} },
                    new NostrEventTag(){TagIdentifier="i",Data= {idx.ToString() } },
                    new NostrEventTag(){TagIdentifier="n",Data= {numOfParts.ToString()} }
                }
            };

            await newEvent.EncryptNip04EventAsync(this._privateKey);
            await newEvent.ComputeIdAndSignAsync(this._privateKey, handlenip4: false);
            await nostrClient.SendEventsAndWaitUntilReceived(new[] { newEvent }, CancellationToken.None);
        }
    }

    public abstract void OnMessage(string senderNodeName, object frame);

    private string _subsriptionId;

    public void Start()
    {
        nostrClient.ConnectAndWaitUntilConnected().Wait();
        this._subsriptionId = "nostr_giggossip_subscription"; //Guid.NewGuid().ToString();
        nostrClient.CreateSubscription(this._subsriptionId, new[]
                {
            new NostrSubscriptionFilter()
            {
                Kinds = new []{3,4,30000},
                ReferencedPublicKeys = new []{ _publicKey.ToHex() }
            }
        }).Wait();
    }

    private Dictionary<string, SortedDictionary<int, string>> _partial_messages = new();


    private async void NostrClient_EventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) args)
    {
        if (args.subscriptionId == this._subsriptionId)
        {
            foreach (var nostrEvent in args.events)
            {
                if (nostrEvent.Kind == 4)
                {
                    Dictionary<string, List<string>> tagDic = new();
                    foreach (var tag in nostrEvent.Tags)
                    {
                        if (tag.Data.Count > 0)
                            tagDic[tag.TagIdentifier] = tag.Data;
                    }
                    if (tagDic.ContainsKey("p"))
                        if (tagDic["p"][0] == _publicKey.ToHex())
                        {
                            int parti = int.Parse(tagDic["i"][0]);
                            int partNum = int.Parse(tagDic["n"][0]);
                            string idx = tagDic["x"][0];
                            var msg = await nostrEvent.DecryptNip04EventAsync(this._privateKey);
                            if (partNum == 1)
                            {
                                var frame = Crypto.DeserializeObject<object>(Convert.FromBase64String(msg));
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
                                        frame = Crypto.DeserializeObject<object>(Convert.FromBase64String(txt));
                                        _partial_messages.Remove(idx);
                                    }
                                }
                                if (frame !=null)
                                    this.OnMessage(nostrEvent.PublicKey, frame);
                            }
                        }
                }
                else if (nostrEvent.Kind == 3)
                {
                    Console.WriteLine(nostrEvent.Tags);
                }
                else if (nostrEvent.Kind == 30000)
                {
                    Console.WriteLine(nostrEvent.Tags);
                }
            }
        }
    }

}

