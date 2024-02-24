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
    const int SettingsKind = 10128;

    CompositeNostrClient nostrClient;
    protected ECPrivKey privateKey;
    protected ECXOnlyPubKey publicKey;
    public string PublicKey;
    public int ChunkSize;
    public string[] NostrRelays { get; private set; }
    private Dictionary<string, Type> _registeredFrameTypes = new();
    private string subscriptionId;
    private bool eoseReceived = false;
    private SemaphoreSlim eventSemSlim = new(1, 1);
    private bool consumeCL;
    protected CancellationTokenSource CancellationTokenSource = new();

    public IFlowLogger FlowLogger { get; private set; }

    public NostrNode(ECPrivKey privateKey, int chunkSize, bool consumeCL)
    {
        this.privateKey = privateKey;
        this.publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this.publicKey.AsHex();
        this.ChunkSize = chunkSize;
        this.consumeCL = consumeCL;
    }

    public NostrNode(NostrNode me, int chunkSize, bool consumeCL)
    {
        this.privateKey = me.privateKey;
        this.publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this.publicKey.AsHex();
        this.ChunkSize = chunkSize;
        this.consumeCL = consumeCL;
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
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__);
            {
                var newEvent = new NostrEvent()
                {
                    Kind = HelloKind,
                    Content = "",
                    Tags = { },
                };
                await newEvent.ComputeIdAndSignAsync(this.privateKey, handlenip4: false);
                await nostrClient.PublishEvent(newEvent, CancellationToken.None);
            }
            await FlowLogger.TraceVoidAsync(this, g__, m__);
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }

    protected async Task PublishContactListAsync(Dictionary<string, NostrContact> contactList)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__, contactList);
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
            await FlowLogger.TraceVoidAsync(this, g__, m__);
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }


    protected async Task SaveSettings(string settings)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__, settings);
            {
                var newEvent = new NostrEvent()
                {
                    Kind = SettingsKind,
                    Content = settings,
                    Tags = { new NostrEventTag() { TagIdentifier = "p", Data = { this.PublicKey } } },
                };
                await newEvent.EncryptNip04EventAsync(this.privateKey, skipKindVerification: true);
                await newEvent.ComputeIdAndSignAsync(this.privateKey, handlenip4: false);
                await nostrClient.PublishEvent(newEvent, CancellationToken.None);
            }
            await FlowLogger.TraceVoidAsync(this, g__, m__);
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }

    public async Task<string> SendMessageAsync(string targetPublicKey, object frame, bool ephemeral, DateTime? expiration = null)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__, targetPublicKey, frame, ephemeral, expiration);
            {
                var message = Convert.ToBase64String(Crypto.SerializeObject(frame));
                var evid = Guid.NewGuid().ToString();

                int numOfParts = 1 + message.Length / ChunkSize;
                List<NostrEvent> events = new();
                for (int idx = 0; idx < numOfParts; idx++)
                {
                    var part = ((idx + 1) * ChunkSize < message.Length) ? message.Substring(idx * ChunkSize, ChunkSize) : message.Substring(idx * ChunkSize);
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
                    await nostrClient.PublishEvent(e, CancellationTokenSource.Token);
                return await FlowLogger.TraceOutAsync(this, g__, m__, evid);
            }
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }

    public abstract Task OnMessageAsync(string eventId, bool isNew, string senderPublicKey, object frame);
    public virtual void OnContactList(string eventId, bool isNew, Dictionary<string, NostrContact> contactList) { }
    public virtual void OnHello(string eventId, bool isNew, string senderPublicKeye) { }
    public virtual void OnSettings(string eventId, bool isNew, string settings) { }
    public virtual void OnEose() { }

    public abstract bool OpenMessage(string id);
    public abstract void CommitMessage(string id);
    public abstract void AbortMessage(string id);

    protected async Task StartAsync(string[] nostrRelays, IFlowLogger flowLogger)
    {
        if (nostrClient != null)
            await StopAsync();
        FlowLogger = flowLogger;
        NostrRelays = nostrRelays;
        nostrClient = new CompositeNostrClient((from rel in nostrRelays select new System.Uri(rel)).ToArray());
        this.eoseReceived = false;
        await nostrClient.ConnectAndWaitUntilConnected();
        nostrClient.EventsReceived += NostrClient_EventsReceived;
        nostrClient.EoseReceived += NostrClient_EoseReceived;
        nostrClient.StateChanged += NostrClient_StateChanged;
        subscriptionId = Guid.NewGuid().ToString();
        await nostrClient.CreateSubscription(subscriptionId, new[]{
                        new NostrSubscriptionFilter()
                        {
                            Kinds = new []{SettingsKind},
                            Authors = new []{ publicKey.ToHex() },
                            ReferencedPublicKeys = new []{ publicKey.ToHex() }
                        },
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

    private async void NostrClient_StateChanged(object? sender, (Uri, System.Net.WebSockets.WebSocketState?) e)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__);
            {
                if ((e.Item2 == System.Net.WebSockets.WebSocketState.CloseReceived) || (e.Item2 == System.Net.WebSockets.WebSocketState.Closed))
                {
                    nostrClient.StateChanged -= NostrClient_StateChanged;
                    while (true)
                    {
                        try
                        {
                            await FlowLogger.TraceWarningAsync(this, g__, m__, "Connection to NOSTR relay lost, reconnecting");
                            await StartAsync(NostrRelays, FlowLogger);
                            await FlowLogger.TraceWarningAsync(this, g__, m__, "Connection to NOSTR restored");
                            break;
                        }
                        catch (System.Net.WebSockets.WebSocketException ex)
                        {
                            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
                            Thread.Sleep(1000);
                        }
                    }
                }
                await FlowLogger.TraceVoidAsync(this, g__, m__);
            }
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }

    private async void NostrClient_EoseReceived(object? sender, string subscriptionId)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__);
            {
                if (subscriptionId == this.subscriptionId)
                {
                    await eventSemSlim.WaitAsync();
                    try
                    {
                        this.eoseReceived = true;
                        OnEose();
                    }
                    finally
                    {
                        eventSemSlim.Release();
                    }
                }
                await FlowLogger.TraceVoidAsync(this, g__, m__);
            }
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }

    private async void NostrClient_EventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) e)
    {
        try
        {
            Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
            try
            {
                await FlowLogger.TraceInAsync(this, g__, m__, e);
                {
                    if (e.subscriptionId == subscriptionId)
                    {
                        await eventSemSlim.WaitAsync();
                        try
                        {
                            foreach (var nostrEvent in e.events)
                            {
                                if (nostrEvent.Kind == SettingsKind)
                                    await ProcessSettingsAsync(nostrEvent);
                                else if (nostrEvent.Kind == HelloKind)
                                    await ProcessHelloAsync(nostrEvent);
                                else if (nostrEvent.Kind == ContactListKind)
                                    await ProcessContactListAsync(nostrEvent);
                                else
                                    await ProcessNewMessageAsync(nostrEvent);
                            }
                        }
                        finally
                        {
                            eventSemSlim.Release();
                        }
                    }
                }
                await FlowLogger.TraceVoidAsync(this, g__, m__);
            }
            catch (Exception ex)
            {
                await FlowLogger.TraceExcAsync(this, g__, m__, ex);
                throw;
            }

        }
        catch
        {
            //ignore all exceptions and move on
        }
    }

    public virtual async Task StopAsync()
    {
        if (nostrClient == null)
            return;
        CancellationTokenSource.Cancel();
        await nostrClient.CloseSubscription(subscriptionId);
        await nostrClient.Disconnect();
        nostrClient.Dispose();
        nostrClient = null;
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
                        return;
                    var frame = Crypto.DeserializeObject(Convert.FromBase64String(msg), t);
                    await this.DoOnMessageAsync(idx, eoseReceived, nostrEvent.PublicKey, frame);
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
                                return;
                            var frame = Crypto.DeserializeObject(Convert.FromBase64String(txt), t);
                            await this.DoOnMessageAsync(idx, eoseReceived, nostrEvent.PublicKey, frame);
                        }
                    }
                }
            }
    }

    private async Task DoOnMessageAsync(string eventId, bool isNew, string senderPublicKey, object frame)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__, eventId, isNew, senderPublicKey, frame);
            {

                if (!OpenMessage(eventId))
                    return;
                try
                {
                    await OnMessageAsync(eventId, isNew, senderPublicKey, frame);
                    CommitMessage(eventId);
                }
                catch (Exception ex)
                {
                    await FlowLogger.TraceExceptionAsync(ex);
                    AbortMessage(eventId);
                }
            }
            await FlowLogger.TraceVoidAsync(this, g__, m__);
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }

    private async Task ProcessContactListAsync(NostrEvent nostrEvent)
    {
        if (!consumeCL)
            return;

        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__, nostrEvent);
            {
                if (!OpenMessage(nostrEvent.Id))
                    return;
                try
                {
                    var newCL = new Dictionary<string, NostrContact>();
                    foreach (var tag in nostrEvent.Tags)
                    {
                        if (tag.TagIdentifier == "p")
                            newCL[tag.Data[0]] = new NostrContact() { PublicKey = this.PublicKey, ContactPublicKey = tag.Data[0], Relay = tag.Data[1], Petname = tag.Data[2] };
                    }
                    OnContactList(nostrEvent.Id, eoseReceived, newCL);
                    CommitMessage(nostrEvent.Id);
                }
                catch (Exception ex)
                {
                    await FlowLogger.TraceExceptionAsync(ex);
                    AbortMessage(nostrEvent.Id);
                }
            }
            await FlowLogger.TraceVoidAsync(this, g__, m__);
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }

    private async Task ProcessHelloAsync(NostrEvent nostrEvent)
    {
        if (!consumeCL)
            return;

        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__, nostrEvent);
            {
                if (!OpenMessage(nostrEvent.Id))
                    return;
                try
                {
                    OnHello(nostrEvent.Id, eoseReceived, nostrEvent.PublicKey);
                }
                catch (Exception ex)
                {
                    await FlowLogger.TraceExceptionAsync(ex);
                    AbortMessage(nostrEvent.Id);
                }
            }
            await FlowLogger.TraceVoidAsync(this, g__, m__);
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }

    private async Task ProcessSettingsAsync(NostrEvent nostrEvent)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__, nostrEvent);
            {
                if (!OpenMessage(nostrEvent.Id))
                    return;
                try
                {
                    var msg = await nostrEvent.DecryptNip04EventAsync(this.privateKey, skipKindVerification: true);
                    OnSettings(nostrEvent.Id, eoseReceived, msg);
                }
                catch (Exception ex)
                {
                    await FlowLogger.TraceExceptionAsync(ex);
                    AbortMessage(nostrEvent.Id);
                }
            }
            await FlowLogger.TraceVoidAsync(this, g__, m__);
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
            throw;
        }
    }
}
