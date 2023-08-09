// See https://aka.ms/new-console-template for more information
using GigLNDWalletAPIClient;
using CryptoToolkit;
using NBitcoin.Secp256k1;

using (var httpClient = new HttpClient())
{
    var baseUrl = "https://localhost:7101/";
    var client = new swaggerClient(baseUrl, httpClient);

    var ecpriv = Context.Instance.CreateECPrivKey(Convert.FromHexString("7f4c11a9742421366e40e321ca50b682c27f7422190c14a487525e69e6048326"));

    string pubkey = ecpriv.CreateXOnlyPubKey().AsHex();

    var guid = await client.GetTokenAsync(pubkey);

    var address= await client.NewAddressAsync(pubkey, Crypto.MakeSignedTimedToken(ecpriv, DateTime.Now, guid), CancellationToken.None);

    var ballance = await client.GetBalanceAsync(pubkey, Crypto.MakeSignedTimedToken(ecpriv, DateTime.Now, guid), CancellationToken.None);

    var inv = await client.AddInvoiceAsync(pubkey, Crypto.MakeSignedTimedToken(ecpriv, DateTime.Now, guid), 1000, "", CancellationToken.None);

}

Console.WriteLine("Hello, World!");
