using CommandLine;
using CommandLine.Text;

namespace RideShareCLIApp;

class Program
{

    public class Options
    {
        [Option('b', "basic", Required = false, HelpText = "Run basic test")]
        public bool Basic { get; set; }
    }

    static void Main(string[] args)
    {
        var parserResult = new Parser(with => { with.IgnoreUnknownArguments = true; with.HelpWriter = null; })
            .ParseArguments<Options>(args)
            .WithParsed(o =>
            {
                if (o.Basic)
                    new GigWorkerBasicTest.BasicTest(args).RunAsync().Wait();
                if (o.Medium)
                    new GigWorkerMediumTest.MediumTest(args).RunAsync().Wait();
                if (o.Complex)
                    new GigWorkerComplexTest.ComplexTest(args).RunAsync().Wait();
            });

        if (parserResult.Tag == ParserResultType.NotParsed)
        {
            var helpText = HelpText.AutoBuild(parserResult, h =>
            {
                h.AdditionalNewLineAfterOption = false;
                h.Heading = "GogWorkerText";
                h.Copyright = "Copyright (C) Don't Trust Verify";
                return h;
            }, e => e);

            Console.WriteLine(helpText);
        }
        else
            Console.WriteLine("END");

        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
        }
    }

}