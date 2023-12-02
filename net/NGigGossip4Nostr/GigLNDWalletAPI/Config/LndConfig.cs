using LNDClient;
using System.Text.Json.Nodes;

namespace GigLNDWalletAPI.Config
{
    public class LndConfig : LND.NodeSettings
    {
        public const string SectionName = nameof(LndConfig);
        public string[] FriendNodes { get; set; }
        public long MaxSatoshisPerChannel { get; set; }
    }
}
