using System;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;
using GigGossipSettlerAPIClient;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using CryptoToolkit;
using Sharprompt;

namespace GigLogView;

public class GigLogView
{
    UserSettings userSettings;
    swaggerClient settlerClient;

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

    public GigLogView(string[] args, string baseDir, string sfx)
    {
        if (sfx == null)
            sfx = AnsiConsole.Prompt(new TextPrompt<string>("Enter the [orange1]config suffix[/]?").AllowEmpty());

        sfx = (string.IsNullOrWhiteSpace(sfx)) ? "" : "_" + sfx;

        IConfigurationRoot config = GetConfigurationRoot(baseDir, args, ".giggossip", "giglogview" + sfx + ".conf");

        this.userSettings = config.GetSection("user").Get<UserSettings>();

        var baseUrl = userSettings.GigSettlerOpenApi;
        settlerClient = new swaggerClient(baseUrl, new HttpClient());
    }


    public enum CommandEnum
    {
        [Display(Name = "Exit App")]
        Exit,
        [Display(Name = "GetSystemLog")]
        GetSystemLog,
    }

    public async Task<string> MakeToken()
    {
        var ecpriv = userSettings.SettlerPrivateKey.AsECPrivKey();
        string pubkey = ecpriv.CreateXOnlyPubKey().AsHex();
        var guid = SettlerAPIResult.Get<Guid>(await settlerClient.GetTokenAsync(pubkey));
        return Crypto.MakeSignedTimedToken(ecpriv, DateTime.UtcNow, guid);
    }


    public async Task RunAsync()
    {
        while (true)
        {
            try
            {
                var cmd = Prompt.Select<CommandEnum>("Select command", pageSize: 6);
                if (cmd == CommandEnum.Exit)
                {
                    if (cmd == CommandEnum.Exit)
                        break;
                }
                else if (cmd == CommandEnum.GetSystemLog)
                {
                    var pubkey = Prompt.Input<string>("PubKey", TextCopy.ClipboardService.GetText());
                    var frm = new DateTimeOffset(DateTime.UtcNow.AddMinutes(-120)).ToUnixTimeMilliseconds();
                    var to = new DateTimeOffset(DateTime.UtcNow.AddMinutes(1)).ToUnixTimeMilliseconds();
                    var r = await settlerClient.GetLogEventsAsync(await MakeToken(), pubkey, frm, to);
                    var res = SettlerAPIResult.Get<List<SystemLogEntry>>(r);
                    foreach(var row in res)
                    {
                        AnsiConsole.WriteLine(row.PublicKey);
                        AnsiConsole.WriteLine(row.EventType.ToString());
                        AnsiConsole.WriteLine(row.DateTime.ToString("yyyyMMdd HHmmss"));
                        AnsiConsole.WriteLine(row.Message);
                        AnsiConsole.WriteLine(row.Exception);
                        AnsiConsole.WriteLine("-----------");
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex,
                    ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes |
                    ExceptionFormats.ShortenMethods | ExceptionFormats.ShowLinks);
            }
        }
    }
}

