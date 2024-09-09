using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CryptoToolkit;

var privkeyx = "3a2d5e66bcac006201687f5cfab18c0d4adbbf5809fd9f79ed79916749351a23";
var pubkey = "a97c15464ee25ca4d1f0908f5aed1f10e41c3c117822f1f3f6e30ad038f869e0";
var token = Guid.Parse("77502787-93c3-4d4f-9e04-a59a5cb7faae");
var dt = DateTime.Parse("2021-01-01");

var authToken = new TimedGuidToken
{
    Token = token.AsUUID(),
    Timestamp = dt.AsUnixTimestamp(),
    PublicKey = pubkey,
    Signature = Google.Protobuf.ByteString.Empty,
};
Console.WriteLine(token);
Console.WriteLine(token.AsUUID().ToArray().AsHex());
Console.WriteLine(new DateTimeOffset(dt).ToUnixTimeSeconds());

var serializedObj = Crypto.BinarySerializeObject(authToken);
Console.WriteLine(serializedObj.AsHex());

Console.WriteLine(NBitcoin.Crypto.Hashes.SHA256( "050607".AsBytes() ).AsHex());

Console.WriteLine(NBitcoin.Crypto.Hashes.SHA256(serializedObj).AsHex());

using var sha256 = System.Security.Cryptography.SHA256.Create();


Span<byte> buf = stackalloc byte[64];
sha256.TryComputeHash(serializedObj, buf, out _);
Console.WriteLine(buf.ToArray().AsHex());

privkeyx.AsECPrivKey().SignBIP340(buf[..32]).WriteToSpan(buf);
Console.WriteLine(buf.ToArray().AsHex());
Console.WriteLine(Convert.ToBase64String(buf));

var authToken2 = new TimedGuidToken
{
    Token = token.AsUUID(),
    Timestamp = dt.AsUnixTimestamp(),
    PublicKey = pubkey,
    Signature = buf.AsByteString(),
};

var mstr = new MemoryStream();
using (var gstr = new Google.Protobuf.CodedOutputStream(mstr))
{
    authToken2.WriteTo(gstr);
}
var serializedObj2 = mstr.ToArray();

Console.WriteLine(serializedObj2.AsHex());

var ttok = CryptoToolkit.TimedGuidToken.Parser.ParseFrom(serializedObj2);

var toks = "020a406139376331353436346565323563613464316630393038663561656431663130653431633363313137383232663166336636653330616430333866383639653010d096b7ff051a107750278793c34d4f9e04a59a5cb7faae22408f86e42d26f2390359c3db9cde4b0b78df9122ad6eaeb0a66230d67a1c4a4206796b8d09cbbfd42cb61d803646a2ec84488fac4eaf04f9ed0774c16fa9b7f73c";


var ttoken = Crypto.BinaryDeserializeObject<TimedGuidToken>(serializedObj2);

var token64 = "AgpAYTk3YzE1NDY0ZWUyNWNhNGQxZjA5MDhmNWFlZDFmMTBlNDFjM2MxMTc4MjJmMWYzZjZlMzBhZDAzOGY4NjllMBDQlrf/BRoQd1Anh5PDTU+eBKWaXLf6riJA6iYsUcCyZcdlu7AQR4gLdET0aIUyxjv2OMrwrqLmBR7Pm5UEJPLgZ6JlKQnUfKwgN71Sdu74k2jGJW9T9JtH4A==";
var verif = Crypto.VerifySignedTimedToken(token64, 600);


var mnemonic = Crypto.GenerateMnemonic();
Console.WriteLine(mnemonic);
var privkey = Crypto.DeriveECPrivKeyFromMnemonic(mnemonic);
Console.WriteLine(privkey.AsHex());
var privkey2 = Crypto.DeriveECPrivKeyFromMnemonic(mnemonic);
Console.WriteLine(privkey2.AsHex());

var hash = Crypto.ComputeSha256(new List<byte[]> { Encoding.ASCII.GetBytes("A"), Encoding.ASCII.GetBytes("B") });

Console.WriteLine(hash.AsHex());


var myPrivKey = Crypto.GeneratECPrivKey();
var otherPrivKey = Crypto.GeneratECPrivKey();
var myPubKey = myPrivKey.CreateXOnlyPubKey();
var otherPubKey = otherPrivKey.CreateXOnlyPubKey();

/*
var encrypted = Crypto.EncryptObject(obj, otherPubKey, myPrivKey);
var decr = Crypto.DecryptObject<OBJ>(encrypted, otherPrivKey, myPubKey);

Console.WriteLine(JsonSerializer.Serialize(decr));
var signature = Crypto.SignObject(obj, myPrivKey);
Console.WriteLine(JsonSerializer.Serialize(signature));
var ok = Crypto.VerifyObject(obj, signature, myPubKey);
Console.WriteLine(ok);

var symKey = Crypto.GenerateSymmetricKey();
var encrypted1 = Crypto.SymmetricObjectEncrypt(symKey,obj);
var decr1 = Crypto.SymmetricObjectDecrypt<OBJ>(symKey,encrypted1);

Console.WriteLine(JsonSerializer.Serialize(decr1));

class OBJ : Google.Protobuf.Message<OBJ>
{
    public string ATR1 { get; set; }

    public string ATR2 { get; set; }

    public string ATR3 { get; set; }
}
*/

