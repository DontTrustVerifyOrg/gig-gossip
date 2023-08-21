using System.Text;
using System.Text.Json;
using CryptoToolkit;
using NBitcoin;
using NBitcoin.Secp256k1;
using NGigGossip4Nostr;

var hash = Crypto.ComputeSha256(new List<byte[]> { Encoding.ASCII.GetBytes("A"), Encoding.ASCII.GetBytes("B") });

Console.WriteLine(hash.AsHex());


var obj = new List<object>() { "ala", new List<object>{ "ma", "kota" } };

Console.WriteLine(JsonSerializer.Serialize(obj));

var myPrivKey = Crypto.GeneratECPrivKey();
var otherPrivKey = Crypto.GeneratECPrivKey();
var myPubKey = myPrivKey.CreateXOnlyPubKey();
var otherPubKey = otherPrivKey.CreateXOnlyPubKey();

var encrypted = Crypto.EncryptObject(obj, otherPubKey, myPrivKey);
var decr = Crypto.DecryptObject< List<object>>(encrypted, otherPrivKey, myPubKey);

Console.WriteLine(JsonSerializer.Serialize(decr));

var signature = Crypto.SignObject(obj, myPrivKey);
Console.WriteLine(JsonSerializer.Serialize(signature));
var ok = Crypto.VerifyObject(obj, signature, myPubKey);
Console.WriteLine(ok);


var symKey = Crypto.GenerateSymmetricKey();

var encrypted1 = Crypto.SymmetricEncrypt(symKey,obj);
var decr1 = Crypto.SymmetricDecrypt<List<object>>(symKey,encrypted1);

Console.WriteLine(JsonSerializer.Serialize(decr1));

var wr = new WorkRequest() { PowScheme = "sha256", PowTarget = ProofOfWork.PowTargetFromComplexity("sha256", 100) };
var pow = wr.ComputeProof(obj);
Console.WriteLine(pow.Nuance);
Console.WriteLine(pow.Validate(obj));

Console.ReadKey();
