﻿using System.Reflection;
using CommandLine;
using CommandLine.Text;
using Spectre.Console;

namespace GigLogView;

class Program
{

    public class Options
    {
        [Option('d', "basedir", Required = false, HelpText = "Configuration folder dir")]
        public string? BaseDir { get; set; }

        [Option('s', "sfx", Required = false, HelpText = "Configuration file suffix")]
        public string? Sfx { get; set; }
    }

    static void Main(string[] args)
    {
        var parserResult = new Parser(with => { with.IgnoreUnknownArguments = true; with.HelpWriter = null; })
            .ParseArguments<Options>(args)
            .WithParsed(options =>
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = "GigLogView.speed.flf";
                    AnsiConsole.WriteLine(assembly.FullName);
                    AnsiConsole.MarkupLine("\t[bold orange1]Gig-Gossip[/] protocol [link]https://gig-gossip.org[/]");
                    AnsiConsole.MarkupLine("\t[bold](C) Don't Trust Verify[/] [link]https://donttrustverify.org[/]");
                    using Stream stream = assembly.GetManifestResourceStream(resourceName);
                    var font = FigletFont.Load(stream);
                    AnsiConsole.Write(
                        new FigletText(font, "log")
                            .LeftJustified()
                            .Color(Color.Green1));

                    AnsiConsole.WriteLine();

                    new GigLogView(args, options.BaseDir, options.Sfx).RunAsync().Wait();
                }
                catch (Exception ex)
                {
                    AnsiConsole.WriteException(ex,
                        ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes |
                        ExceptionFormats.ShortenMethods | ExceptionFormats.ShowLinks);
                    throw;
                }
            });

        if (parserResult.Tag == ParserResultType.NotParsed)
        {
            var helpText = HelpText.AutoBuild(parserResult, h =>
            {
                h.AdditionalNewLineAfterOption = false;
                h.Heading = "GigLogView";
                h.Copyright = "Copyright (C) Don't Trust Verify";
                return h;
            }, e => e);

            Console.WriteLine(helpText);
        }
        else
            Console.WriteLine("END");

    }
}


public class UserSettings
{
    public required string GigSettlerOpenApi { get; set; }
    public required string SettlerPrivateKey { get; set; }
}
