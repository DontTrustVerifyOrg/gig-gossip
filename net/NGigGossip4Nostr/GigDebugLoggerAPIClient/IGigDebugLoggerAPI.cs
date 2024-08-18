using System;
namespace GigDebugLoggerAPIClient;

public interface IGigDebugLoggerAPI
{
    Task<Result> LogEventAsync(string apikey, string pubkey, string eventType, FileParameter message, System.Threading.CancellationToken cancellationToken);
}

public partial class swaggerClient : IGigDebugLoggerAPI
{
}