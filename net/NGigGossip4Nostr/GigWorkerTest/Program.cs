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
        //       new GigWorkerBasicTest.BasicTest(args).Run();
        // new GigWorkerMediumTest.MediumTest(args).Run();
        new GigWorkerComplexTest.ComplexTest(args).Run();
    }

}

