using System;
using System.Text;
using System.Xml.Linq;



namespace NGigTaxiLib;

public class TaxiTopic : AbstractTopic
{
    public string FromGeohash { get; set; }
    public string ToGeohash { get; set; }
    public DateTime PickupAfter { get; set; }
    public DateTime DropoffBefore { get; set; }
}

