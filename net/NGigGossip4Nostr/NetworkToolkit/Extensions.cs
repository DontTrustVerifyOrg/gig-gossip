using Microsoft.AspNetCore.Http;
namespace NetworkToolkit;

public static class Extensions
{
    public async static Task<byte[]> ToBytes(this IFormFile formFile)
    {
        using var stream = new MemoryStream();
        await formFile.CopyToAsync(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream.ToArray();
    }

}
