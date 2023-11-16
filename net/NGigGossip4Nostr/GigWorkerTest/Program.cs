using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using NGigGossip4Nostr;
using NGigTaxiLib;
using NGeoHash;
using NBitcoin.Protocol;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using CommandLine;

internal class Program
{

    public class Options
    {
        [Option('b', "basic", Required = false, HelpText = "Run basic test")]
        public bool Basic { get; set; }
        [Option('m', "medium", Required = false, HelpText = "Run medium test")]
        public bool Medium { get; set; }
        [Option('c', "complex", Required = false, HelpText = "Run complex test")]
        public bool Complex { get; set; }
    }

    static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
                           .WithParsed<Options>(o =>
                           {
                               if (o.Basic)
                                   new GigWorkerBasicTest.BasicTest(args).RunAsync().Wait();
                               if (o.Medium)
                                   new GigWorkerMediumTest.MediumTest(args).RunAsync().Wait();
                               if (o.Complex)
                                   new GigWorkerComplexTest.ComplexTest(args).RunAsync().Wait();
                           });
        Console.WriteLine("END");
    }
}

