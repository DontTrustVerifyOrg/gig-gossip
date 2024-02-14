using System;
using NBitcoin.Protocol;

namespace GigGossipSettlerAPI;

public static class Extensions
{
    public async static Task<byte[]> ToBytes(this IFormFile formFile)
    {
        using var stream = new MemoryStream();
        await formFile.CopyToAsync(stream);
        return stream.ToArray();
    }
}

