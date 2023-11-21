using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RideShareCLIApp;

class Program
{

    public class Options
    {
        [Option('i', "id", Required = true, HelpText = "Id of the player")]
        public string Id { get; set; }

        [Option('d', "basedir", Required = false, HelpText = "Configuration folder dir")]
        public string? BaseDir { get; set; }

        [Option('s', "script", Required = false, HelpText = "Script to run")]
        public string Script { get; set; }

        [Option('v', "verbose", Required = false, HelpText = "Enable verbose output.")]
        public bool Verbose { get; set; }
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
                var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder
                    .ClearProviders()
                        .AddConsole()
                        .AddDebug();

                    if (options.Verbose)
                    {
                        builder.SetMinimumLevel(LogLevel.Debug);
                    }
                });

                var logger = loggerFactory.CreateLogger<Program>();

                try
                {
                    new RideShareCLIApp(logger, options.Id, options.Script, GetConfigurationRoot(options.BaseDir, args, ".giggossip", "ridesharecli.conf")).RunAsync().Wait();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.GetExceptionFootprints());
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