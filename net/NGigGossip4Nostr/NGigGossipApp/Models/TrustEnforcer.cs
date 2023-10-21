using CryptoToolkit;

namespace GigMobile.Models
{
	public class TrustEnforcer
    {
        public string Uri { get; set; }
        public string PhoneNumber { get; set; }
        public Certificate Certificate { get; set; }
    }
}

