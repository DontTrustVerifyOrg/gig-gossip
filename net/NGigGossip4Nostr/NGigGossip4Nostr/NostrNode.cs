using System;
using System.Diagnostics;
using NBitcoin.Secp256k1;
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
using GigDebugLoggerAPIClient;
using Nostr.Client.Messages.Contacts;
using GigGossip;

namespace NGigGossip4Nostr;


public abstract class NostrNode
{
    NostrMultiWebsocketClient nostrClient;
    protected ECPrivKey privateKey;
    protected ECXOnlyPubKey publicKey;
    public string PublicKey;
    public int ChunkSize;
    public HashSet<string> NostrRelays { get; private set; }
    private HashSet<string> TempRelays = new();
    private string SubscriptionId;
    private bool consumeCL;
    protected CancellationTokenSource CancellationTokenSource = new();
    public IRetryPolicy RetryPolicy;
    LogWrapper<NostrNode> TRACE = FlowLoggerFactory.Trace<NostrNode>();
    public bool Started => nostrClient != null;

    object waitingForEvent = new object();
    string waitedEventId;
    string ackMessage = "";
    bool eventReceived = false;
    bool eventAccepted = false;

    const int PING_TIMER_INTER = 10000;
    private System.Timers.Timer _myPingTimer = new System.Timers.Timer(PING_TIMER_INTER);
    private DateTime _myPingTimerLastPing = DateTime.MinValue;

    public event EventHandler<ServerConnectionStateEventArgs> OnServerConnectionState;

    public NostrNode(ECPrivKey privateKey, int chunkSize, bool consumeCL, IRetryPolicy retryPolicy)
    {
        this.privateKey = privateKey;
        this.publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this.publicKey.AsHex();
        this.ChunkSize = chunkSize;
        this.consumeCL = consumeCL;
        this.RetryPolicy = retryPolicy;
        _myPingTimerLastPing = DateTime.MinValue;
        _myPingTimer.Elapsed += _myPingTimer_Elapsed;
    }

    public NostrNode(NostrNode me, int chunkSize, bool consumeCL)
    {
        this.privateKey = me.privateKey;
        this.publicKey = privateKey.CreateXOnlyPubKey();
        this.PublicKey = this.publicKey.AsHex();
        this.ChunkSize = chunkSize;
        this.consumeCL = consumeCL;
        this.RetryPolicy = me.RetryPolicy;
        _myPingTimer.Elapsed += _myPingTimer_Elapsed;
    }

    public async static Task TryConnectingToRelayAsync(Uri relay, CancellationToken cancellationToken)
    {
        using var communicator = new NostrWebsocketCommunicator(relay);
        using var client = new NostrWebsocketClient(communicator, null);
        await communicator.StartOrFail();
        client.Dispose();
        communicator.Dispose();
    }

    public async Task SayHelloAsync()
    {
        using var TL = TRACE.Log();
        try
        {

            SendEvent(() => {
                var newEvent = new NostrEvent()
                {
                    Kind = NostrKind.GigGossipHelloKind,
                    CreatedAt = DateTime.UtcNow,
                    Content = "",
                    Tags = { },
                };
                return newEvent.Sign(NostrPrivateKey.FromEc(this.privateKey));
            }
            , 1, false);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private async void _myPingTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
        using var TL = TRACE.Log();
        try
        {
            bool sayHello = false;
            lock (_myPingTimer)
            {
                if ((DateTime.UtcNow - _myPingTimerLastPing).TotalMilliseconds > PING_TIMER_INTER / 2)
                    sayHello = true;
                _myPingTimerLastPing = DateTime.UtcNow;
            }
            if (sayHello)
                await SayHelloAsync();
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
        }
    }

    void EventAck(string eventId,bool accepted,string message)
    {
        lock(waitingForEvent)
        {
            using var TL = TRACE.Log().Args(eventId, accepted);
            lock (_myPingTimer)
            {
                _myPingTimerLastPing = DateTime.UtcNow;
            }
            if (eventId == waitedEventId)
            {
                eventReceived = true;
                eventAccepted = accepted;
                ackMessage = message;
                Monitor.PulseAll(waitingForEvent);
            }
        }
    }

    void SendEvent(Func<NostrEvent> eventFactory, int retryCount,bool throwOnFailure)
    {
        using var TL = TRACE.Log().Args(retryCount, throwOnFailure);
        try
        {
            for (int i = 0; i < retryCount; i++)
            {
                lock (waitingForEvent)
                {
                    var e = eventFactory();
                    waitedEventId = e.Id;
                    eventReceived = false;
                    eventAccepted = false;
                    nostrClient.Send(new NostrEventRequest(e));
                    while (true)
                    {
                        if (!Monitor.Wait(waitingForEvent, 60000))
                        {
                            TL.Warning("Timeout");
                            break;
                        }
                        if (eventReceived)
                        {
                            if (eventAccepted)
                                return;
                            else
                            {
                                TL.Warning($"Not Accepted {ackMessage}");
                                break;
                            }
                        }
                    }
                }
                Thread.Sleep(1000);
            }
            if (throwOnFailure)
                throw new Exception(ackMessage);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    protected async Task SaveSettings(string settings)
    {
        using var TL = TRACE.Log().Args(settings);
        try
        {
            SendEvent(()=>
            {
                var newEvent = new NostrEvent()
                {
                    Kind = NostrKind.GigGossipSettingsKind,
                    CreatedAt = DateTime.UtcNow,
                    Content = settings,
                    Tags = new NostrEventTags(new NostrEventTag("p", this.PublicKey)),
                };
                var encrypted = NostrEncryptedEvent.Encrypt(newEvent, NostrPrivateKey.FromEc(this.privateKey), NostrKind.GigGossipSettingsKind);
                return encrypted.Sign(NostrPrivateKey.FromEc(this.privateKey));
            }, 5, true);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task<string> SendMessageAsync(string targetPublicKey, Frame frame, bool ephemeral, DateTime? expiration = null)
    {
        using var TL = TRACE.Log().Args(targetPublicKey, frame, ephemeral, expiration);
        try
        {
            var message = Convert.ToBase64String(Crypto.BinarySerializeObject(frame));
            var evid = Guid.NewGuid().ToString();

            int numOfParts = 1 + message.Length / ChunkSize;
            List<NostrEvent> events = new();
            for (int idx = 0; idx < numOfParts; idx++)
            {
                var part = ((idx + 1) * ChunkSize < message.Length) ? message.Substring(idx * ChunkSize, ChunkSize) : message.Substring(idx * ChunkSize);
                var tags = new List<NostrEventTag> {
                    new NostrEventTag("p", targetPublicKey),
                    new NostrEventTag("x", evid),
                    new NostrEventTag("i", idx.ToString()),
                    new NostrEventTag("n", numOfParts.ToString())
                };
                if (expiration != null)
                    tags.Add(new NostrEventTag("expiration", ((DateTimeOffset)expiration.Value).ToUnixTimeSeconds().ToString()));

                var kind = ephemeral ? NostrKind.GigGossipEphemeralMessageKind : NostrKind.GigGossipMessageKind;

                var eventFactory = () =>
                {
                    var newEvent = new NostrEvent()
                    {
                        Kind = kind,
                        CreatedAt = DateTime.UtcNow,
                        Content = part,
                        Tags = new NostrEventTags(tags)
                    };
                    var encrypted = newEvent.Encrypt(NostrPrivateKey.FromEc(this.privateKey), NostrPublicKey.FromHex(targetPublicKey), kind);
                    return encrypted.Sign(NostrPrivateKey.FromEc(this.privateKey));
                };

                if (ephemeral)
                    SendEvent(eventFactory, 1, false);
                else
                    SendEvent(eventFactory, 5, true);
            }
            return TL.Ret(evid);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public abstract Task OnMessageAsync(string eventId, string senderPublicKey, Frame frame);
    public virtual void OnHello(string senderPublicKeye, DateTime createdAt) { }
    public virtual void OnSettings(string eventId, string settings) { }

    public abstract bool OpenMessage(string id);
    public abstract bool CommitMessage(string id, int kind, DateTime createdAt);
    public abstract bool AbortMessage(string id);

    public abstract DateTime? GetLastMessageCreatedAt(int kind, int secondsBefore);

    protected async Task StartAsync(string[] nostrRelays)
    {
        using var TL = TRACE.Log().Args(nostrRelays);
        try
        {
            if (nostrClient != null)
                await StopAsync();

            if (OnServerConnectionState != null)
                OnServerConnectionState.Invoke(this, new ServerConnectionStateEventArgs { State = ServerConnectionState.Connecting });

            SubscriptionId = Guid.NewGuid().ToString();

            CancellationTokenSource = new();
            NostrRelays = new(nostrRelays);
            var relays = (from rel in NostrRelays select new NostrWebsocketCommunicator(new System.Uri(rel))).ToArray();
            foreach (var relay in relays)
            {
                relay.Name = relay.Url.Host;
                relay.ReconnectTimeout = TimeSpan.FromSeconds(30);
                relay.ErrorReconnectTimeout = TimeSpan.FromSeconds(60);
                relay.ReconnectionHappened.Subscribe((e) => NostrClient_ReconnectionHappened(relay.Name, relay.Url, e));
                relay.DisconnectionHappened.Subscribe((e) => NostrClient_DisconnectionHappened(relay.Name, relay.Url, e));
            }

            nostrClient = new NostrMultiWebsocketClient(NullLogger<NostrWebsocketClient>.Instance, relays);
            await RetryPolicy.WithRetryPolicy(async () => relays.ToList().ForEach(relay => relay.Start()));

            nostrClient.Streams.EventStream.Subscribe(NostrClient_EventsReceived);
            nostrClient.Streams.OkStream.Subscribe(NostrClient_OkReceived);

            if (OnServerConnectionState != null)
                OnServerConnectionState.Invoke(this, new ServerConnectionStateEventArgs { State = ServerConnectionState.Open });

            _myPingTimer.Start();
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    public async Task AddTempRelaysAsync(string[] nostrRelays)
    {
        foreach (var nostrRelay in nostrRelays)
        {
            if (!NostrRelays.Contains(nostrRelay))
            {
                if (!TempRelays.Contains(nostrRelay))
                {
                    var relay = new NostrWebsocketCommunicator(new System.Uri(nostrRelay));
                    relay.Name = relay.Url.Host;
                    relay.ReconnectTimeout = TimeSpan.FromSeconds(30);
                    relay.ErrorReconnectTimeout = TimeSpan.FromSeconds(60);
                    relay.ReconnectionHappened.Subscribe((e) => NostrClient_ReconnectionHappened(relay.Name, relay.Url, e));
                    relay.DisconnectionHappened.Subscribe((e) => NostrClient_DisconnectionHappened(relay.Name, relay.Url, e));
                    nostrClient.RegisterCommunicator(relay);
                    await RetryPolicy.WithRetryPolicy(relay.Start);
                    TempRelays.Add(nostrRelay);
                }
            }
        }
    }


    public virtual async Task StopAsync()
    {
        using var TL = TRACE.Log();
        try
        {
            if (nostrClient == null)
            {
                TL.Warning("No NOSTR client to stop");
                return;
            }
            _myPingTimer.Stop();
            Unsubscribe();
            foreach(var nostrRelay in TempRelays)
            {
                nostrClient.RemoveRegistration(nostrRelay);
            }
            nostrClient.Dispose();
            nostrClient = null;
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private void Subscribe(INostrClient client)
    {
        using var TL = TRACE.Log();
        try
        {
            
            client.Send(new NostrRequest4(SubscriptionId,
                new NostrFilter
                {
                    Kinds = new[] { NostrKind.GigGossipSettingsKind },
                    Authors = new[] { publicKey.AsHex() },
                    P = new[] { publicKey.AsHex() },
                    Since = GetLastMessageCreatedAt((int)NostrKind.GigGossipSettingsKind, 10), 
                },
                new NostrFilter
                {
                    Kinds = new[] { NostrKind.GigGossipHelloKind },
                    Since = GetLastMessageCreatedAt((int)NostrKind.GigGossipHelloKind, 10), 
                },
                new NostrFilter
                {
                    Kinds = new[] { NostrKind.GigGossipMessageKind },
                    P = new[] { publicKey.AsHex() },
                    Since = GetLastMessageCreatedAt((int)NostrKind.GigGossipMessageKind, 10), 
                },
                new NostrFilter
                {
                    Kinds = new[] { NostrKind.GigGossipEphemeralMessageKind },
                    P = new[] { publicKey.AsHex() }
                }));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private void Unsubscribe()
    {
        using var TL = TRACE.Log();
        try
        {
            nostrClient.Send(new NostrCloseRequest(SubscriptionId));
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private async void NostrClient_DisconnectionHappened(string relayName, Uri uri, Websocket.Client.DisconnectionInfo e)
    {
        using var TL = TRACE.Log().Args(relayName, uri, e);
        try
        {
            TL.Warning("Connection to NOSTR relay lost");
            if (e.Type != Websocket.Client.DisconnectionType.NoMessageReceived)
                OnServerConnectionState?.Invoke(this, new ServerConnectionStateEventArgs { State = ServerConnectionState.Quiet, Uri = uri });
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
        }
    }

    private async void NostrClient_ReconnectionHappened(string relayName, Uri uri, Websocket.Client.ReconnectionInfo e)
    {
        using var TL = TRACE.Log().Args(relayName, uri, e);
        try
        {
            if(nostrClient==null)
            {
                TL.Error("Client is null");
                return;
            }
            var client = nostrClient.FindClient(relayName);
            if (client == null)
            {
                TL.Error("Client Not Found");
                return;
            }
            Subscribe(client);
            if (e.Type == Websocket.Client.ReconnectionType.Initial)
                TL.Warning("Connected to NOSTR");
            else
                TL.Warning("Reconnecting to NOSTR");

            OnServerConnectionState?.Invoke(this, new ServerConnectionStateEventArgs { State = ServerConnectionState.Open, Uri = uri });
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
        }
    }

    private void NostrClient_OkReceived(NostrOkResponse e)
    {
        using var TL = TRACE.Log().Args(e);
        try
        {
            if (!e.Accepted)
                TL.Warning(e.Message);
            EventAck(e.EventId,e.Accepted,e.Message);
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
        }
    }

    private async void NostrClient_EventsReceived(NostrEventResponse e)
    {
        using var TL = TRACE.Log().Args(e);
        try
        {
            lock (_myPingTimer)
            {
                _myPingTimerLastPing = DateTime.UtcNow;
            }
            if (e.Subscription == SubscriptionId)
            {
                var nostrEvent = e.Event;
                if ((nostrEvent.Kind == NostrKind.GigGossipSettingsKind) && nostrEvent is NostrEncryptedEvent encryptedSettingsNostrEvent)
                    await ProcessSettingsAsync(encryptedSettingsNostrEvent);
                else if (nostrEvent.Kind == NostrKind.GigGossipHelloKind)
                    await ProcessHelloAsync(nostrEvent);
                else if (nostrEvent is NostrEncryptedEvent encryptedNostrEvent)
                    await ProcessNewMessageAsync(encryptedNostrEvent);
            }
            else
            {
                TL.Warning($"Subscription ID mismatch {SubscriptionId} != {e.Subscription}");
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
        }
    }

    private ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _partial_messages = new();

    private async Task ProcessNewMessageAsync(NostrEncryptedEvent nostrEvent)
    {
        using var TL = TRACE.Log().Args(nostrEvent);
        try
        {
            Dictionary<string, List<string>> tagDic = new();
            foreach (var tag in nostrEvent.Tags)
            {
                if (tag.AdditionalData.Count() > 0)
                    tagDic[tag.TagIdentifier] = tag.AdditionalData.ToList();
            }
            if (tagDic.ContainsKey("p"))
                if (tagDic["p"][0] == publicKey.AsHex())
                {
                    int parti = int.Parse(tagDic["i"][0]);
                    int partNum = int.Parse(tagDic["n"][0]);
                    string idx = tagDic["x"][0];
                    var msg = nostrEvent.DecryptContent(NostrPrivateKey.FromEc(this.privateKey));
                    if (partNum == 1)
                    {
                        var frame = Crypto.BinaryDeserializeObject<Frame>(Convert.FromBase64String(msg));
                        await this.DoOnMessageAsync(idx, nostrEvent.CreatedAt.Value, nostrEvent.Pubkey, frame);
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
                                var frame = Crypto.BinaryDeserializeObject<Frame>(Convert.FromBase64String(txt));
                                await this.DoOnMessageAsync(idx, nostrEvent.CreatedAt.Value, nostrEvent.Pubkey, frame);
                            }
                        }
                    }
                }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private async Task DoOnMessageAsync(string eventId, DateTime createdAt, string senderPublicKey, Frame frame)
    {
        using var TL = TRACE.Log().Args(eventId, senderPublicKey, frame);
        try
        {
            if (!OpenMessage(eventId))
                return;
            try
            {
                OnHello(senderPublicKey,createdAt);
                await OnMessageAsync(eventId, senderPublicKey, frame);
                CommitMessage(eventId, (int) NostrKind.GigGossipMessageKind, createdAt);
            }
            catch (Exception ex)
            {
                AbortMessage(eventId);
                TL.Exception(ex);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private async Task ProcessHelloAsync(NostrEvent nostrEvent)
    {
        if (!consumeCL)
            return;

        using var TL = TRACE.Log().Args(nostrEvent);
        try
        {
            if (!OpenMessage(nostrEvent.Id))
                return;
            try
            {
                OnHello(nostrEvent.Pubkey, nostrEvent.CreatedAt ?? DateTime.MinValue);
                CommitMessage(nostrEvent.Id, (int)NostrKind.GigGossipHelloKind, nostrEvent.CreatedAt.Value);
            }
            catch (Exception ex)
            {
                AbortMessage(nostrEvent.Id);
                TL.Exception(ex);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }

    private async Task ProcessSettingsAsync(NostrEncryptedEvent nostrEvent)
    {
        using var TL = TRACE.Log().Args(nostrEvent);
        try
        {
            if (!OpenMessage(nostrEvent.Id))
                return;
            try
            {
                var msg = nostrEvent.DecryptContent(NostrPrivateKey.FromEc(this.privateKey));
                OnSettings(nostrEvent.Id, msg);
                CommitMessage(nostrEvent.Id, (int) NostrKind.GigGossipSettingsKind, nostrEvent.CreatedAt.Value);
            }
            catch (Exception ex)
            {
                TL.Exception(ex);
                AbortMessage(nostrEvent.Id);
            }
        }
        catch (Exception ex)
        {
            TL.Exception(ex);
            throw;
        }
    }
}
