using CryptoToolkit;
using Microsoft.Maui.Controls;
using System.Windows.Input;

namespace GigMobile.Models
{
	public class TrustEnforcer
    {
        public string Uri { get; set; }
        public string PhoneNumber { get; set; }
        public string Name { get; set; }
        public Certificate Certificate { get; set; }
    }
}

