﻿using System.Diagnostics;
using Newtonsoft.Json;
using Nostr.Client.Json;
using Nostr.Client.Keys;
using Nostr.Client.Messages.Direct;
using Nostr.Client.Utils;

namespace Nostr.Client.Messages
{
    [DebuggerDisplay("{CreatedAt} {Kind.ToString()} {Pubkey}")]
    public class NostrEvent : IEquatable<NostrEvent>
    {
        [JsonExtensionData]
        private Dictionary<string, object?> _additionalData = new();

        /// <summary>
        /// 32-bytes lowercase hex-encoded sha256 of the the serialized event data
        /// </summary>
        [ArrayProperty(0)]
        public string? Id { get; init; }

        /// <summary>
        /// 32-bytes lowercase hex-encoded public key of the event creator
        /// </summary>
        [ArrayProperty(1)]
        public string? Pubkey { get; init; }

        [JsonProperty("created_at")]
        [ArrayProperty(2)]
        public DateTime? CreatedAt { get; init; }

        [ArrayProperty(3)]
        public NostrKind Kind { get; init; }

        [ArrayProperty(4)]
        public NostrEventTags? Tags { get; init; } = NostrEventTags.Empty;

        /// <summary>
        /// Arbitrary string
        /// </summary>
        [ArrayProperty(5)]
        public string? Content { get; init; }

        /// <summary>
        /// 64-bytes hex of the signature of the sha256 hash of the serialized event data, which is the same as the "id" field
        /// </summary>
        public string? Sig { get; init; }

        /// <summary>
        /// Additional unparsed data
        /// </summary>
        [JsonIgnore]
        public IReadOnlyDictionary<string, object?> AdditionalData
        {
            get => _additionalData;
            init
            {
                _additionalData = value as Dictionary<string, object?> ??
                                  value.ToDictionary(x => x.Key, x => x.Value);
            }
        }

        /// <summary>
        /// Clone event and all property hierarchy
        /// </summary>
        public NostrEvent DeepClone()
        {
            return DeepClone(Id, Sig);
        }

        /// <summary>
        /// Clone event with custom Id and Signature
        /// </summary>
        public NostrEvent DeepClone(string? id, string? signature)
        {
            return DeepClone(id, signature, Pubkey);
        }

        /// <summary>
        /// Clone event with custom Id, Signature and PubKey
        /// </summary>
        public NostrEvent DeepClone(string? id, string? signature, string? pubKey)
        {
            return DeepClone(id, signature, pubKey, Tags);
        }

        /// <summary>
        /// Clone event with custom Id, Signature, PubKey and tags
        /// </summary>
        public NostrEvent DeepClone(string? id, string? signature, string? pubKey, NostrEventTags? tags)
        {
            return new NostrEvent
            {
                Id = id,
                Pubkey = pubKey,
                CreatedAt = CreatedAt,
                Kind = Kind,
                Tags = tags?.DeepClone() ?? NostrEventTags.Empty,
                Content = Content,
                Sig = signature,
                _additionalData = _additionalData.ToDictionary(x => x.Key, y => y.Value)
            };
        }

        /// <summary>
        /// Clone event with custom pubKey, compute a new Id automatically
        /// </summary>
        public NostrEvent DeepCloneWithPubKey(string? pubKey)
        {
            var ev = DeepClone(null, null, pubKey);
            var id = ev.ComputeId();
            return ev.DeepClone(id, null);
        }

        /// <summary>
        /// Clone event with custom signature
        /// </summary>
        public NostrEvent DeepCloneWithSignature(string? signature)
        {
            var ev = DeepClone(Id, signature, Pubkey);
            return ev;
        }

        public bool Equals(NostrEvent? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id == other.Id;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((NostrEvent)obj);
        }

        public override int GetHashCode()
        {
            return Id != null ? Id.GetHashCode() : 0;
        }

        /// <summary>
        /// Compute unique id based on event data
        /// </summary>
        public string ComputeId()
        {
            var id = ComputeIdBytes();
            var hex = id.ToHex();
            return hex;
        }

        /// <summary>
        /// Return Id if not null, otherwise compute a new one from the event data
        /// </summary>
        public string GetOrComputeId()
        {
            return string.IsNullOrWhiteSpace(Id) ? ComputeId() : Id;
        }

        /// <summary>
        /// Compute signature of this event by given private key.
        /// PubKey is not modified! 
        /// Returns signature in hex format.
        /// </summary>
        public string? ComputeSignature(NostrPrivateKey privateKey)
        {
            var id = GetOrComputeId();
            var signature = privateKey.SignHex(id);
            return signature;
        }

        /// <summary>
        /// Sign this event by given private key and compose a new deep clone.
        /// </summary>
        public NostrEvent Sign(NostrPrivateKey privateKey)
        {
            var publicKey = privateKey.DerivePublicKey();
            var clone = DeepCloneWithPubKey(publicKey.Hex);

            var signature = clone.ComputeSignature(privateKey);
            return clone.DeepCloneWithSignature(signature);
        }

        /// <summary>
        /// Validate signature of this event
        /// </summary>
        public bool IsSignatureValid()
        {
            if (string.IsNullOrWhiteSpace(Pubkey))
                return false;
            var publicKey = NostrPublicKey.FromHex(Pubkey);
            return publicKey.IsHexSignatureValid(Sig, GetOrComputeId());
        }

        /// <summary>
        /// Encrypt event into direct message event (kind 4)
        /// </summary>
        public NostrEncryptedEvent EncryptDirect(NostrPrivateKey sender, NostrPublicKey receiver)
        {
            var receiverPubkey = receiver.Hex;
            var tagsWithReceiver = Tags ?? new NostrEventTags();
            if (!tagsWithReceiver.ContainsProfile(receiverPubkey))
                tagsWithReceiver = tagsWithReceiver.DeepClone(NostrEventTag.Profile(receiverPubkey));

            var clone = DeepClone(null, null, sender.Hex, tagsWithReceiver);
            return NostrEncryptedEvent.EncryptDirectMessage(clone, sender);
        }

        /// <summary>
        /// Encrypt event, kind will be taken from the given event or can be overriden by the parameter
        /// </summary>
        public NostrEncryptedEvent Encrypt(NostrPrivateKey sender, NostrPublicKey receiver, NostrKind? kind = null)
        {
            var receiverPubkey = receiver.Hex;
            var tagsWithReceiver = Tags ?? new NostrEventTags();
            if (!tagsWithReceiver.ContainsProfile(receiverPubkey))
                tagsWithReceiver = tagsWithReceiver.DeepClone(NostrEventTag.Profile(receiverPubkey));

            var clone = DeepClone(null, null, sender.Hex, tagsWithReceiver);
            return NostrEncryptedEvent.Encrypt(clone, sender, kind);
        }

        private byte[] ComputeIdBytes()
        {
            var clone = DeepClone("<<leading_zero>>", null);
            var json = JsonConvert.SerializeObject(clone, NostrSerializer.ArraySettings);

            // we need to include id=0 as a number instead of string
            json = ReplaceValue(json, "\"<<leading_zero>>\"", "0");

            var hash = HashExtensions.GetSha256(json);
            return hash;
        }

        private static string ReplaceValue(string json, string value, string replacement)
        {
            return json.Replace(value, replacement);
        }
    }
}
