using System;

namespace GigGossip;

public partial class JobRequest
{
    public async Task<bool> VerifyAsync(ICertificationAuthorityAccessor caAccessor, TimeSpan timestampTolerance, CancellationToken cancellationToken)
    {
        if (!this.Header.Header.IsStillValid(timestampTolerance))
            return false;
        var caPubKey = await caAccessor.GetPubKeyAsync(this.Header.Header.AuthorityUri.AsUri(), cancellationToken);
        return Crypto.VerifyObject(this.Header, this.Signature.Value.ToArray(), caPubKey);
    }
}

