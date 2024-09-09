using System;
using CryptoToolkit;
using NBitcoin;
using NBitcoin.Secp256k1;

namespace GigGossip;

public partial class AuthToken
{
    /// <summary>
    /// Creates a signed timed token using a provided private key, date time and guid.
    /// </summary>
    public static string Create(ECPrivKey ecpriv, DateTime dateTime, Guid guid)
    {
        var tt = new AuthTokenHeader()
        {
            PublicKey = ecpriv.CreateXOnlyPubKey().AsPublicKey(),
            Timestamp = dateTime.AsUnixTimestamp(),
            TokenId = guid.AsUUID(),
        };

        return Convert.ToBase64String(Crypto.BinarySerializeObject(
            new AuthToken
            {
                Header = tt,
                Signature = tt.Sign(ecpriv)
            }));
    }

    /// <summary>
    /// Verifies the validity of a signed timed token. Returns the timed token if it is valid within a given period of seconds. Returns null otherwise.
    /// </summary>
    public static AuthToken? Verify(string authTokenBase64, double seconds)
    {
        AuthToken timedToken = Crypto.BinaryDeserializeObject<AuthToken>(Convert.FromBase64String(authTokenBase64));

        if ((DateTimeOffset.UtcNow - timedToken.Header.Timestamp.AsUtcDateTime()).Seconds > seconds)
            return null;

        return timedToken.Header.Verify(
            timedToken.Signature,
            timedToken.Header.PublicKey.AsECXOnlyPubKey()
            ) ? timedToken : null;
    }

}