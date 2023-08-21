using System;
using System.Text;
using System.Xml.Linq;



namespace NGigTaxiLib;

[Serializable]
public class TaxiTopic
{
    public required string FromGeohash { get; set; }
    public required string ToGeohash { get; set; }
    public DateTime PickupAfter { get; set; }
    public DateTime DropoffBefore { get; set; }
}

