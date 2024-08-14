using System;
using System.Text;
using System.Xml.Linq;
using ProtoBuf;
using CryptoToolkit;


namespace NGigTaxiLib;

[ProtoContract]
public class TaxiTopic:IProtoFrame
{
    [ProtoMember(1)]
    public required string FromGeohash { get; set; }
    [ProtoMember(2)]
    public required string ToGeohash { get; set; }
    [ProtoMember(3)]
    public required DateTime PickupAfter { get; set; }
    [ProtoMember(4)]
    public required DateTime DropoffBefore { get; set; }
}

