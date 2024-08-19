namespace GigDebugLoggerAPIClient;

public static class FlowLoggerFactory
{
    static FlowLogger defaultInstance = new FlowLogger();

    public static void Initialize(bool traceEnabled, string pubkey, Uri loggerUri, Func<HttpClient> httpFactory)
    {
        defaultInstance.Initialize(traceEnabled, pubkey, loggerUri, httpFactory);
    }

    public static bool Enabled { get => defaultInstance.Enabled; set => defaultInstance.Enabled = value; }   

    public static LogWrapper<T> Trace<T>()
    {
        return new LogWrapper<T>(defaultInstance);
    }
}
