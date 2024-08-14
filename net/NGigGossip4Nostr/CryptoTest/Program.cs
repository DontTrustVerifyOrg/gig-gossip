using System.Text;
using System.Text.Json;
using CryptoToolkit;
using NBitcoin;
using NBitcoin.Secp256k1;
using NGigGossip4Nostr;
using ProtoBuf;

var mnemonic = Crypto.GenerateMnemonic();
Console.WriteLine(mnemonic);
var privkey = Crypto.DeriveECPrivKeyFromMnemonic(mnemonic);
Console.WriteLine(privkey.AsHex());
var privkey2 = Crypto.DeriveECPrivKeyFromMnemonic(mnemonic);
Console.WriteLine(privkey2.AsHex());

var hash = Crypto.ComputeSha256(new List<byte[]> { Encoding.ASCII.GetBytes("A"), Encoding.ASCII.GetBytes("B") });

Console.WriteLine(hash.AsHex());

var obj = new OBJ{ ATR1= "ala", ATR2= "ma", ATR3="kota" } ;

Console.WriteLine(JsonSerializer.Serialize(obj));

var myPrivKey = Crypto.GeneratECPrivKey();
var otherPrivKey = Crypto.GeneratECPrivKey();
var myPubKey = myPrivKey.CreateXOnlyPubKey();
var otherPubKey = otherPrivKey.CreateXOnlyPubKey();

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

[ProtoContract]
class OBJ : IProtoFrame
{
    [ProtoMember(1)]
    public string ATR1 { get; set; }

    [ProtoMember(2)]
    public string ATR2 { get; set; }

    [ProtoMember(3)]
    public string ATR3 { get; set; }
}