using NBitcoin.Secp256k1;
using CryptoToolkit;
namespace CryptoToolkit;

/// <summary>
/// Represents a serializable object that can be signed with a private key and verified with a public key.
/// </summary>
[Serializable]
public class SignableObject
{
    /// <summary>
    /// the signature of the object. The signature is created using a private key.
    /// </summary>
    public byte[] Signature { get; set; }

    /// <summary>
    /// Sign the current instance of <see cref="SignableObject"/> with a given private key.
    /// After this method is called, the Signature property will hold the created signature.
    /// </summary>
    /// <param name="privateKey">An ECPrivKey instance representing the private key to use for signing the object.</param>
    protected void Sign(ECPrivKey privateKey)
    {
        Signature = Crypto.SignObject(this, privateKey);
    }

    /// <summary>
    /// Verify the current instance of <see cref="SignableObject"/> with a given public key.
    /// </summary>
    /// <param name="publicKey">An ECXOnlyPubKey instance representing the public key to use for verifying the object.</param>
    /// <returns>
    /// <code>true</code> if the verification was successful; otherwise, <code>false</code>.
    /// </returns>
    protected bool Verify(ECXOnlyPubKey publicKey)
    {
        var signature = Signature;
        Signature = null;
        var result = Crypto.VerifyObject(this, signature, publicKey);
        Signature = signature;
        return result;
    }
}