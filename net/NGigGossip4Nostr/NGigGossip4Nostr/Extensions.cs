using System;

namespace NGigGossip4Nostr;

public static class Extensions
{
    public async static Task<byte[]> ToBytes(this GigGossipSettlerAPIClient.FileParameter formFile)
    {
        using var stream = new MemoryStream();
        await formFile.Data.CopyToAsync(stream);
        formFile.Data.Seek(0, SeekOrigin.Begin);
        return stream.ToArray();
    }
}

