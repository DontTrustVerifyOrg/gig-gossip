// See https://aka.ms/new-console-template for more information
using NBitcoin.Secp256k1;
using NNostr.Client;
using NNostr.Client.Crypto;
using NNostr.Client.Protocols;
using NNostr.Client.JsonConverters;
using NBitcoin;
using NBitcoin.Protocol;
using System;

var nostrRelays = new[]
{
    "ws://127.0.0.1:6969"
};

var privKeys = new[]{
    "6b27e718f7599c64cc1dc88fffa352b211dabf22a019d0487ec7f9bce97dae7e",
    "0e83ff018c99293f454604a8a7c665a4913ff14ea02d9561ffb382cf97f7eabb",
    "c5cc3d2a3e4eb450e659f715bc17dfc998dde7195f4ce71ce90591282811400b",
    "23a18521f97a9caf2467553bf0c08b2440f742265184e6004132ec5a6f87e95c",
    "81fd4a05830ed199671cb84f3b1a4cdf4339e59a02a4e2ea91b53775a3b9138e",
    "023c37dae47fca0a64b3ab5b9b64f647d971141451a46fe03e0f4b37cf239d48",
    "a746ac8138f4e078455da02daad51661ffb5124e36205531f8b1f91fd7db0697",
    "050069e61e8863285ca7c285e8b08179f70be424f366b87823189cf2f67f6ba2",
    "836e85219d6c38208507b20150f443fc3d8813e966e8462399c057cad1e93d17",
    "6d28ff42b621d3eb1c9550a7d4e0c23bf765c7152c81b3d86d4b121bbc50b4db",
    "344ffadccc1e178e730ca7489a24be64f322b1047ac345bd519525d6c3811bf5",
};

var petnames = new[] {
    "Sonof Satoshi",
    "Mia Robertson",
    "Lucas Peterson",
    "Noah Carter",
    "Ava Garcia",
    "Ethan Johnson",
    "Emily Thompson",
    "Luke Harrison",
    "Scarlett Baker",
    "Leo Alexander",
    "Grace Nicholson"
};


var mainId = int.Parse(args[0]);
var mainPrivKey = privKeys[mainId];
var mainKey = Context.Instance.CreateECPrivKey(Convert.FromHexString(mainPrivKey));
var mainPubKey = mainKey.CreateXOnlyPubKey();

var client = new CompositeNostrClient((from rel in nostrRelays select new System.Uri(rel)).ToArray());
await client.ConnectAndWaitUntilConnected();


void OnClientOnEventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) args)
{
    if (args.subscriptionId == "my-subscription-id")
    {
        foreach (var nostrEvent in args.events)
        {
            if (nostrEvent.Kind == 4)
            {
                foreach (var tag in nostrEvent.Tags)
                {
                    if (tag.TagIdentifier == "p")
                        if (tag.Data[0] == mainPubKey.ToHex())
                        {
                            var msg = nostrEvent.DecryptNip04EventAsync(mainKey);
                            Console.WriteLine(mainId.ToString() + "$" + msg);
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

client.EventsReceived += OnClientOnEventsReceived;

//subscribe to all kind 1 events under the subscription id of "my-subscription-id"
await client.CreateSubscription("my-subscription-id", new[]
        {
            new NostrSubscriptionFilter()
            {
                Kinds = new []{3,4,30000},
                ReferencedPublicKeys = new []{ mainPubKey.ToHex() }
            }
});

while (true)
{
    Console.Write("?");
    string? line = Console.ReadLine();
    if (line != null)
    {
        var prts = line.Split(':');
        if (prts.Length >= 2)
        {
            var id = int.Parse(prts[0]);
            var message = prts[1];
            var otherPrivKey = Context.Instance.CreateECPrivKey(Convert.FromHexString(privKeys[id]));
            var otherPubKey = otherPrivKey.CreateXOnlyPubKey();

            var newEvent = new NostrEvent()
            {
                Kind = 4,
                Content = message,
                Tags = { new NostrEventTag() { TagIdentifier = "p", Data = { otherPubKey.ToHex() } } }
            };

            await newEvent.EncryptNip04EventAsync(mainKey);
            // sign the event
            await newEvent.ComputeIdAndSignAsync(mainKey, handlenip4: false); // already handled 
            // send the event
            await client.SendEventsAndWaitUntilReceived(new[] { newEvent }, CancellationToken.None);
        }
        else
        {
            var friendList = new List<NostrEventTag>();
            for (var i = 0; i < petnames.Length; i++)
            {
                var petname = petnames[i];
                var otherPrivKey = Context.Instance.CreateECPrivKey(Convert.FromHexString(privKeys[i]));
                var otherPubKey = otherPrivKey.CreateXOnlyPubKey();
                friendList.Add(new NostrEventTag() { TagIdentifier = "p", Data = { otherPubKey.ToHex(), nostrRelays[0], petname } });
            }
            {
                var newEvent = new NostrEvent()
                {
                    Kind = 3,
                    Content = "",
                    Tags = friendList
                };
                // sign the event
                await newEvent.ComputeIdAndSignAsync(mainKey, handlenip4: false); // already handled 
                                                                                  // send the event
                await client.SendEventsAndWaitUntilReceived(new[] { newEvent }, CancellationToken.None);
            }
            {
                var message = friendList[0].ToString();
                var newEvent = new NostrEvent()
                {
                    Kind = 4,
                    Content = message,
                    Tags = { new NostrEventTag() { TagIdentifier = "p", Data = { mainPubKey.ToHex() } } }
                };
                await newEvent.EncryptNip04EventAsync(mainKey);
                newEvent.Tags.Add(
                    new NostrEventTag() { TagIdentifier = "d", Data = { "gig-gossip" } }
                );
                newEvent.Kind = 30000;
                // sign the event
                await newEvent.ComputeIdAndSignAsync(mainKey, handlenip4: false); // already handled 
                // send the event
                await client.SendEventsAndWaitUntilReceived(new[] { newEvent }, CancellationToken.None);

            }
        }
    }
}

