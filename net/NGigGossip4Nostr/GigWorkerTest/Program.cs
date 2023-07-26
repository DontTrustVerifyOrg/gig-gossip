using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using NGigGossip4Nostr;
using NGigTaxiLib;
using CommandLine;
using NGeoHash;
using NBitcoin.Protocol;
using System.Diagnostics;

namespace GigWorkerTest;

internal class Program
{

    class Options
    {
        [Option('r', "read", Required = true, HelpText = "Input files to be processed.")]
        public IEnumerable<string> InputFiles { get; set; }

        // Omitting long name, defaults to name of property, ie "--verbose"
        [Option(
          Default = false,
          HelpText = "Prints all messages to standard output.")]
        public bool Verbose { get; set; }

        [Option("stdin",
          Default = false,
          HelpText = "Read from stdin")]
        public bool stdin { get; set; }

        [Value(0, MetaName = "offset", HelpText = "File offset.")]
        public long? Offset { get; set; }
    }

    static void Main(string[] args)
    {
        RunOptions(null);
        return;
        Parser.Default.ParseArguments<Options>(args)
          .WithParsed(RunOptions)
          .WithNotParsed(HandleParseError);
    }

    static void HandleParseError(IEnumerable<Error> errs)
    {
        //handle errors
    }

    static void RunOptions(Options opts)
    {
        //ComplexTest test = new ComplexTest();
        MidTest test = new MidTest();
        //BasicTest test = new BasicTest();
        test.Run();
    }


}

