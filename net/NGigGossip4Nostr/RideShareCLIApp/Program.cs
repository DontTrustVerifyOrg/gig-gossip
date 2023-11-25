using System.Reflection;
using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace RideShareCLIApp;

class Program
{

    public class Options
    {
        [Option('i', "id", Required = false, HelpText = "Id of the player")]
        public string Id { get; set; }

        [Option('d', "basedir", Required = false, HelpText = "Configuration folder dir")]
        public string? BaseDir { get; set; }

        /*
        [Option('s', "script", Required = false, HelpText = "Script to run")]
        public string Script { get; set; }

        [Option('v', "verbose", Required = false, HelpText = "Enable verbose output.")]
        public bool Verbose { get; set; }
        *///NOT YET
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
            .WithParsed(options =>
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = "RideShareCLIApp.speed.flf";

                    AnsiConsole.WriteLine(assembly.FullName);
                    AnsiConsole.MarkupLine("[bold orange1]Gig-Gossip[/] protocol [italic blue]https://gig-gossip.org[/]");
                    AnsiConsole.MarkupLine("[bold](C) Don't Trust Verify[/] [italic blue]https://donttrustverify.org[/]");
                    using Stream stream = assembly.GetManifestResourceStream(resourceName);
                    var font = FigletFont.Load(stream);
                    AnsiConsole.Write(
                        new FigletText(font, "rideshare")
                            .LeftJustified()
                            .Color(Color.Green1));

                    AnsiConsole.WriteLine();

                    new RideShareCLIApp(options.Id, GetConfigurationRoot(options.BaseDir, args, ".giggossip", "ridesharecli.conf")).RunAsync().Wait();
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
                h.Heading = "RideShareCLIApp";
                h.Copyright = "Copyright (C) Don't Trust Verify";
                return h;
            }, e => e);

            Console.WriteLine(helpText);
        }
        else
            Console.WriteLine("END");

    }

}