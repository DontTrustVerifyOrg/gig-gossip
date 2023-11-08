using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using NGigGossip4Nostr;
using NGigTaxiLib;
using NGeoHash;
using NBitcoin.Protocol;
using System.Diagnostics;


internal class Program
{

    static void Main(string[] args)
    {
        //new GigWorkerBasicTest.BasicTest(args).RunAsync().Wait();
        new GigWorkerMediumTest.MediumTest(args).RunAsync().Wait();
        //new GigWorkerComplexTest.ComplexTest(args).RunAsync().Wait();
    }

}

