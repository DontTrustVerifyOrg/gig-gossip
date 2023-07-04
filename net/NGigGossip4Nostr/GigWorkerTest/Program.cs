using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using NGigGossip4Nostr;
using NGigTaxiLib;
using CommandLine;
using NGeoHash;
using NBitcoin.Protocol;

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
        var ca = Cert.CreateCertificationAuthority("CA");
        var settlerPrivKey = Crypto.GeneratECPrivKey();
        var setter_certificate = ca.IssueCertificate(settlerPrivKey.CreateXOnlyPubKey(), "is_ok", true, DateTime.Now.AddDays(7), DateTime.Now.AddDays(-7));
        var settler = new Settler("ST",setter_certificate, settlerPrivKey, 12);

        var gigWorker = new GigWorker("GigWorker1", ca, 1, settler);
        var customer = new Customer("Customer1", ca, 1, settler);

        gigWorker.ConnectTo(customer);

        gigWorker.Start();
        customer.Start();

        customer.Go();

        //gigWorker.Stop();
        //customer.Stop();

        gigWorker.Join();
        customer.Join();

    }
}

