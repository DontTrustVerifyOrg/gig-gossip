using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using NGigGossip4Nostr;
using RideShareFrames;
using NGeoHash;
using NBitcoin.Protocol;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using CommandLine;
using CommandLine.Text;
using Spectre.Console;

internal class Program
{
    public class Options
    {
        [Option('d', "basedir", Required = false, HelpText = "Configuration folder dir")]
        public string? BaseDir { get; set; }

        [Option('b', "basic", Required = false, HelpText = "Run basic test")]
        public bool Basic { get; set; }

        [Option('m', "medium", Required = false, HelpText = "Run medium test")]
        public bool Medium { get; set; }

        [Option('c', "complex", Required = false, HelpText = "Run complex test")]
        public bool Complex { get; set; }
    }

    static IConfigurationRoot GetConfigurationRoot(string? basePath, string[] args, string defaultFolder, string iniName)
    {
        if (basePath == null)
        {
            basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");
            if (basePath == null)
                basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), defaultFolder);
        }
        var builder = new ConfigurationBuilder();
        builder.SetBasePath(basePath)
               .AddIniFile(iniName)
               .AddEnvironmentVariables()
               .AddCommandLine(args);

        return builder.Build();
    }
    static void Main(string[] args)
    {
        var parserResult = new Parser(with => { with.IgnoreUnknownArguments = true; with.HelpWriter = null; })
            .ParseArguments<Options>(args)
            .WithParsed(o =>
            {
                try
                {
                    if (o.Basic)
                        new GigWorkerBasicTest.BasicTest(GetConfigurationRoot(o.BaseDir, args, ".giggossip", "basictest.conf")).RunAsync().Wait();
                    if (o.Medium)
                        new GigWorkerMediumTest.MediumTest(GetConfigurationRoot(o.BaseDir, args, ".giggossip", "mediumtest.conf")).RunAsync().Wait();
                    if (o.Complex)
                        new GigWorkerComplexTest.ComplexTest(GetConfigurationRoot(o.BaseDir, args, ".giggossip", "complextest.conf")).RunAsync().Wait();
                }
                catch(Exception ex)
                {
                    AnsiConsole.WriteException(ex);
                    throw;
                }
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
    }
}

