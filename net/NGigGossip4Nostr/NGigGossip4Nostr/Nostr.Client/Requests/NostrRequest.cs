using Newtonsoft.Json;
using Nostr.Client.Json;
using Nostr.Client.Messages;

namespace Nostr.Client.Requests
{
    [JsonConverter(typeof(ArrayConverter))]
    public class NostrRequest
    {
        public NostrRequest(string subscription, NostrFilter nostrFilter)
        {
            Subscription = subscription;
            NostrFilter = nostrFilter;
        }

        [ArrayProperty(0)]
        public string Type { get; init; } = NostrMessageTypes.Request;

        [ArrayProperty(1)]
        public string Subscription { get; init; }

        [ArrayProperty(2)]
        public NostrFilter NostrFilter { get; init; }
    }

    [JsonConverter(typeof(ArrayConverter))]
    public class NostrRequest4
    {
        public NostrRequest4(string subscription, NostrFilter nostrFilter1, NostrFilter nostrFilter2, NostrFilter nostrFilter3, NostrFilter nostrFilter4)
        {
            Subscription = subscription;
            NostrFilter1 = nostrFilter1;
            NostrFilter2 = nostrFilter2;
            NostrFilter3 = nostrFilter3;
            NostrFilter4 = nostrFilter4;
        }

        [ArrayProperty(0)]
        public string Type { get; init; } = NostrMessageTypes.Request;

        [ArrayProperty(1)]
        public string Subscription { get; init; }

        [ArrayProperty(2)]
        public NostrFilter NostrFilter1 { get; init; }
        [ArrayProperty(3)]
        public NostrFilter NostrFilter2 { get; init; }
        [ArrayProperty(4)]
        public NostrFilter NostrFilter3 { get; init; }
        [ArrayProperty(5)]
        public NostrFilter NostrFilter4 { get; init; }
    }
}
