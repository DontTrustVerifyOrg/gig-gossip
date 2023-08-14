using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using NGigGossip4Nostr;
using NGigTaxiLib;
using NGeoHash;
using NBitcoin.Protocol;
using System.Diagnostics;

namespace GigWorkerTest;

internal class Program
{

    static void Main(string[] args)
    {
        new BasicTest(args).Run().Wait();
    }

}

