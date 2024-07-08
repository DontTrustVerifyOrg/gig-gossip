using System;
using System.Diagnostics;
using NBitcoin.Secp256k1;
using CryptoToolkit;
using System.Threading;
using NBitcoin.RPC;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using Microsoft.AspNetCore.SignalR.Client;
using NetworkClientToolkit;
using System.Data;
using Nostr.Client.Client;
using Nostr.Client.Messages;
using GoogleApi.Entities.Search.Common;
using Nostr.Client.Communicator;
using Nostr.Client.Keys;
using Nostr.Client.Messages.Direct;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reactive.Linq;
using Nostr.Client.Responses;
using System.IO;
using Nostr.Client.Requests;

namespace NGigGossip4Nostr;



public abstract class NostrNode
{
    NostrMultiWebsocketClient nostrClient;
    protected ECPrivKey privateKey;
    protected ECXOnlyPubKey publicKey;
    public string PublicKey;
    public int ChunkSize;
    public string[] NostrRelays { get; private set; }
    private Dictionary<string, Type> _registeredFrameTypes = new();
    private string SubscriptionId;
    private bool eoseReceived = false;
    private SemaphoreSlim eventSemSlim = new(1, 1);
    private ConcurrentQueue<NostrEvent> nostrEvents = new();
    private ConcurrentQueue<NostrEvent> nostrEventsSend = new();
    private bool consumeCL;
    protected CancellationTokenSource CancellationTokenSource = new();
    public IRetryPolicy RetryPolicy;
    private ServerListener serverListener = new();

    public IFlowLogger FlowLogger { get; private set; }
    public bool Started => nostrClient != null;

    public event EventHandler<ServerConnectionStateEventArgs> OnServerConnectionState;

    public NostrNode(ECPrivKey privateKey, int chunkSize, bool consumeCL, IRetryPolicy retryPolicy)
    {
        this.privateKey = privateKey;
        this.publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this.publicKey.AsHex();
        this.ChunkSize = chunkSize;
        this.consumeCL = consumeCL;
        this.RetryPolicy = retryPolicy;
    }

    public NostrNode(NostrNode me, int chunkSize, bool consumeCL)
    {
        this.privateKey = me.privateKey;
        this.publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this.publicKey.AsHex();
        this.ChunkSize = chunkSize;
        this.consumeCL = consumeCL;
        this.RetryPolicy = me.RetryPolicy;
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

    public async static Task TryConnectingToRelayAsync(Uri relay, CancellationToken cancellationToken)
    {
        using var communicator = new NostrWebsocketCommunicator(relay);
        using var client = new NostrWebsocketClient(communicator, null);
        await communicator.StartOrFail();
        client.Dispose();
        communicator.Dispose();
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
                    Kind = NostrKind.HelloKind,
                    CreatedAt = DateTime.UtcNow,
                    Content = "",
                    Tags = { },
                };
                EnqueueForSend(new[] { newEvent.Sign(NostrPrivateKey.FromEc(this.privateKey)) });
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
                NostrEventTags tags;
                lock (contactList)
                {
                    tags = new NostrEventTags((from c in contactList.Values select new NostrEventTag("p", c.ContactPublicKey, c.Relay, c.Petname)).ToArray());
                }
                var newEvent = new NostrEvent()
                {
                    Kind = NostrKind.Contacts,
                    CreatedAt = DateTime.UtcNow,
                    Content = "",
                    Tags = tags,
                };
                EnqueueForSend(new[] { newEvent.Sign(NostrPrivateKey.FromEc(this.privateKey)) });
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
                    Kind = NostrKind.SettingsKind,
                    CreatedAt = DateTime.UtcNow,
                    Content = settings,
                    Tags = new NostrEventTags(new NostrEventTag("p", this.PublicKey)),
                };
                var encrypted = NostrEncryptedEvent.Encrypt(newEvent, NostrPrivateKey.FromEc(this.privateKey), NostrKind.SettingsKind);
                EnqueueForSend(new[] { encrypted.Sign(NostrPrivateKey.FromEc(this.privateKey)) });
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
                    var tags = new List<NostrEventTag> {
                        new NostrEventTag("p", targetPublicKey),
                        new NostrEventTag("x", evid),
                        new NostrEventTag("t", frame.GetType().Name),
                        new NostrEventTag("i", idx.ToString()),
                        new NostrEventTag("n", numOfParts.ToString())
                    };
                    if (expiration != null)
                        tags.Add(new NostrEventTag("expiration", ((DateTimeOffset)expiration.Value).ToUnixTimeSeconds().ToString()));

                    var kind = ephemeral ? NostrKind.EphemeralMessageKind : NostrKind.EncryptedDm;
                    var newEvent = new NostrEvent()
                    {
                        Kind = kind,
                        CreatedAt = DateTime.UtcNow,
                        Content = part,
                        Tags = new NostrEventTags(tags)
                    };

                    var encrypted = newEvent.Encrypt(NostrPrivateKey.FromEc(this.privateKey), NostrPublicKey.FromHex(targetPublicKey), kind);
                    events.Add(encrypted.Sign(NostrPrivateKey.FromEc(this.privateKey)));
                }
                EnqueueForSend(events.ToArray());
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
    public abstract bool CommitMessage(string id);
    public abstract void AbortMessage(string id);

    protected async Task StartAsync(string[] nostrRelays, IFlowLogger flowLogger)
    {
        if (nostrClient != null)
            await StopAsync();

        if (OnServerConnectionState != null)
            OnServerConnectionState.Invoke(this, new ServerConnectionStateEventArgs { State = ServerConnectionState.Connecting });

        StartEventThread();
        StartSendingEventThread();

        SubscriptionId = Guid.NewGuid().ToString();

        CancellationTokenSource = new();
        FlowLogger = flowLogger;
        NostrRelays = nostrRelays;
        var relays = (from rel in nostrRelays select new NostrWebsocketCommunicator(new System.Uri(rel))).ToArray();
        foreach (var relay in relays)
        {
            relay.Name = relay.Url.Host;
            relay.ReconnectTimeout = TimeSpan.FromSeconds(30);
            relay.ErrorReconnectTimeout = TimeSpan.FromSeconds(60);
            relay.ReconnectionHappened.Subscribe((e) => NostrClient_ReconnectionHappened(relay.Name, relay.Url, e));
            relay.DisconnectionHappened.Subscribe((e) => NostrClient_DisconnectionHappened(relay.Name, relay.Url, e));
        }

        nostrClient = new NostrMultiWebsocketClient(NullLogger<NostrWebsocketClient>.Instance, relays);
        this.eoseReceived = false;
        await RetryPolicy.WithRetryPolicy(async () => relays.ToList().ForEach(relay => relay.Start()));

        var events = nostrClient.Streams.EventStream.Where(x => x.Event != null);
        events.Subscribe(NostrClient_EventsReceived);
        //        nostrClient.Streams.NoticeStream.Subscribe(NostrClient_NoticeReceived);
        //        nostrClient.OkStream.NoticeStream.Subscribe(NostrClient_OkReceived)
        nostrClient.Streams.EoseStream.Subscribe(NostrClient_EoseReceived);
        //nostrClient.Streams.UnknownMessageStream.Subscribe(NostrClient_UnknownMessageStream);
        //        ForwardStream(client, client.Streams.UnknownRawStream, Streams.UnknownRawSubject);

        if (OnServerConnectionState != null)
            OnServerConnectionState.Invoke(this, new ServerConnectionStateEventArgs { State = ServerConnectionState.Open });

    }

    private void Subscribe(INostrClient client)
    {
        client.Send(new NostrRequest5(SubscriptionId,
            new NostrFilter
            {
                Kinds = new[] { NostrKind.SettingsKind },
                Authors = new[] { publicKey.AsHex() },
                P = new[] { publicKey.AsHex() },
            },
            new NostrFilter
            {
                Kinds = new[] { NostrKind.HelloKind },
            },
            new NostrFilter
            {
                Kinds = new[] { NostrKind.Contacts },
                Authors = new[] { publicKey.AsHex() },
            },
            new NostrFilter
            {
                Kinds = new[] { NostrKind.EncryptedDm },
                P = new[] { publicKey.AsHex() }
            },
            new NostrFilter
            {
                Kinds = new[] { NostrKind.EphemeralMessageKind },
                P = new[] { publicKey.AsHex() }
            }));
    }

    private void Unsubscribe()
    {
        nostrClient.Send(new NostrCloseRequest(SubscriptionId));
    }

    private async void NostrClient_DisconnectionHappened(string relayName, Uri uri, Websocket.Client.DisconnectionInfo e)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__);
            {
                await FlowLogger.TraceWarningAsync(this, g__, m__, "Connection to NOSTR relay lost");
                OnServerConnectionState?.Invoke(this, new ServerConnectionStateEventArgs { State = e.Type != Websocket.Client.DisconnectionType.NoMessageReceived ? ServerConnectionState.Closed : ServerConnectionState.Quiet, Uri = uri });
                await FlowLogger.TraceVoidAsync(this, g__, m__);
            }
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
        }
    }

    private async void NostrClient_ReconnectionHappened(string relayName, Uri uri, Websocket.Client.ReconnectionInfo e)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__);
            {
                var client = nostrClient.FindClient(relayName);
                if (client != null)
                {
                    this.eoseReceived = false;
                    Subscribe(client);
                }
                else
                {
                    await FlowLogger.TraceWarningAsync(this, g__, m__, "Client Not Found");
                }
                if (e.Type == Websocket.Client.ReconnectionType.Initial)
                    await FlowLogger.TraceWarningAsync(this, g__, m__, "Connected to NOSTR");
                else
                    await FlowLogger.TraceWarningAsync(this, g__, m__, "Reconnecting to NOSTR");
                OnServerConnectionState?.Invoke(this, new ServerConnectionStateEventArgs { State = ServerConnectionState.Open, Uri = uri });
                await FlowLogger.TraceVoidAsync(this, g__, m__);
            }
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
        }
    }

    private async void NostrClient_EoseReceived(NostrEoseResponse e)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__);
            {
                if (e.Subscription == SubscriptionId)
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

    Thread monitorThread;
    private object queueMonitor = new object();
    private bool EventThreadClosing = false;

    private void StartEventThread()
    {
        EventThreadClosing = false;

        monitorThread = new Thread(async () =>
        {
            while (!EventThreadClosing)
            {
                List<NostrEvent> events = new();
                lock (queueMonitor)
                {
                    if (nostrEvents.IsEmpty)
                        Monitor.Wait(queueMonitor);
                    if (EventThreadClosing)
                        return;
                    while (nostrEvents.TryDequeue(out var nostrEvent))
                        events.Add(nostrEvent);
                }
                foreach (var nostrEvent in events)
                {
                    if ((nostrEvent.Kind == NostrKind.SettingsKind) && nostrEvent is NostrEncryptedEvent encryptedSettingsNostrEvent)
                        await ProcessSettingsAsync(encryptedSettingsNostrEvent);
                    else if (nostrEvent.Kind == NostrKind.HelloKind)
                        await ProcessHelloAsync(nostrEvent);
                    else if (nostrEvent.Kind == NostrKind.Contacts)
                        await ProcessContactListAsync(nostrEvent);
                    else if (nostrEvent is NostrEncryptedEvent encryptedNostrEvent)
                        await ProcessNewMessageAsync(encryptedNostrEvent);
                }
            }
        });
        monitorThread.Start();
    }

    void NotifyEventThreadClosing()
    {
        lock (queueMonitor)
        {
            EventThreadClosing = true;
            Monitor.PulseAll(queueMonitor);
        }
    }

    Thread monitorThreadSend;
    private object queueMonitorSend = new object();
    private bool EventThreadClosingSend = false;

    private void StartSendingEventThread()
    {
        EventThreadClosingSend = false;

        monitorThreadSend = new Thread(async () =>
        {
            while (!EventThreadClosingSend)
            {
                List<NostrEvent> events = new();
                lock (queueMonitorSend)
                {
                    if (nostrEventsSend.IsEmpty)
                        Monitor.Wait(queueMonitorSend);
                    if (EventThreadClosingSend)
                        return;
                    while (nostrEventsSend.TryDequeue(out var nostrEvent))
                        events.Add(nostrEvent);
                }
                foreach (var e in events)
                {
                    await RetryPolicy.WithRetryPolicy(async () => nostrClient.Send(new NostrEventRequest(e)));
                }
            }
        });
        monitorThreadSend.Start();
    }

    void EnqueueForSend(NostrEvent[] nostrEvents)
    {
        lock (queueMonitorSend)
        {
            foreach (var e in nostrEvents)
                nostrEventsSend.Enqueue(e);
            Monitor.PulseAll(queueMonitorSend);
        }
    }

    void NotifyEventThreadSendClosing()
    {
        lock (queueMonitorSend)
        {
            EventThreadClosingSend = true;
            Monitor.PulseAll(queueMonitorSend);
        }
    }

    private async void NostrClient_EventsReceived(NostrEventResponse e)
    {
        Guid? g__ = null; string? m__ = null; if (FlowLogger.Enabled) { g__ = Guid.NewGuid(); m__ = FlowLogger.MetNam(); }
        try
        {
            await FlowLogger.TraceInAsync(this, g__, m__, e);
            {
                if (e.Subscription == SubscriptionId)
                {
                    lock (queueMonitor)
                    {
                        nostrEvents.Enqueue(e.Event);
                        Monitor.PulseAll(queueMonitor);
                    }
                }
            }
            await FlowLogger.TraceVoidAsync(this, g__, m__);
        }
        catch (Exception ex)
        {
            await FlowLogger.TraceExcAsync(this, g__, m__, ex);
        }
    }

    public virtual async Task StopAsync()
    {
        if (nostrClient == null)
            return;
        Unsubscribe();
        NotifyEventThreadClosing();
        NotifyEventThreadSendClosing();
        nostrClient.Dispose();
        nostrClient = null;
    }


    private ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _partial_messages = new();

    private async Task ProcessNewMessageAsync(NostrEncryptedEvent nostrEvent)
    {
        Dictionary<string, List<string>> tagDic = new();
        foreach (var tag in nostrEvent.Tags)
        {
            if (tag.AdditionalData.Count() > 0)
                tagDic[tag.TagIdentifier] = tag.AdditionalData.ToList();
        }
        if (tagDic.ContainsKey("p") && tagDic.ContainsKey("t"))
            if (tagDic["p"][0] == publicKey.AsHex())
            {
                int parti = int.Parse(tagDic["i"][0]);
                int partNum = int.Parse(tagDic["n"][0]);
                string idx = tagDic["x"][0];
                var msg = nostrEvent.DecryptContent(NostrPrivateKey.FromEc(this.privateKey));
                if (partNum == 1)
                {
                    var t = GetFrameType(tagDic["t"][0]);
                    if (t == null)
                        return;
                    var frame = Crypto.DeserializeObject(Convert.FromBase64String(msg), t);
                    await this.DoOnMessageAsync(idx, eoseReceived, nostrEvent.Pubkey, frame);
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
                            await this.DoOnMessageAsync(idx, eoseReceived, nostrEvent.Pubkey, frame);
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
                            newCL[tag.AdditionalData.First()] = new NostrContact()
                            {
                                PublicKey = this.PublicKey,
                                ContactPublicKey = tag.AdditionalData.First(),
                                Relay = tag.AdditionalData.Skip(1).First(),
                                Petname = tag.AdditionalData.Skip(2).First()
                            };
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
                    OnHello(nostrEvent.Id, eoseReceived, nostrEvent.Pubkey);
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

    private async Task ProcessSettingsAsync(NostrEncryptedEvent nostrEvent)
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
                    var msg = nostrEvent.DecryptContent(NostrPrivateKey.FromEc(this.privateKey));
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
