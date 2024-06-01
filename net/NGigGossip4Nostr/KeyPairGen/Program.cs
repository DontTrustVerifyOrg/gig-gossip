using CryptoToolkit;
using NBitcoin.Secp256k1;
using CommandLine;
using CommandLine.Text;
using Spectre.Console;
using System.Reflection;

namespace KeyPairGen;

class Program
{

    public class Options
    {
        [Option('m', "mnemonic", Required = false, HelpText = "Mnemonic")]
        public string? Mnemonic { get; set; }

        [Option('p', "privatekey", Required = false, HelpText = "PrivateKey")]
        public string? PrivateKey { get; set; }
    }

    static void Main(string[] args)
    {
        var parserResult = new Parser(with => { with.IgnoreUnknownArguments = true; with.HelpWriter = null; })
            .ParseArguments<Options>(args)
            .WithParsed(options =>
            {
                try
                {
                    string mnemonic;
                    ECPrivKey privKey;
                    if(options.Mnemonic==null)
                    {
                        mnemonic = CryptoToolkit.Crypto.GenerateMnemonic();
                        AnsiConsole.WriteLine(mnemonic);
                    }
                    else
                        mnemonic = options.Mnemonic;

                    if(options.PrivateKey==null)
                    {
                        privKey = CryptoToolkit.Crypto.DeriveECPrivKeyFromMnemonic(mnemonic);
                        AnsiConsole.WriteLine(privKey.AsHex());
                    }
                    else
                        privKey = options.PrivateKey.AsECPrivKey();

                    var pubKey = privKey.CreateXOnlyPubKey();
                    AnsiConsole.WriteLine(pubKey.AsHex());
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
                h.Heading = "KeyPairGen - Generate a new key pair";
                h.Copyright = "Copyright (C) Don't Trust Verify";
                return h;
            }, e => e);

            Console.WriteLine(helpText);
        }
    }

}


