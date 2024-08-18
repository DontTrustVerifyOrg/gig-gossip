namespace GigDebugLoggerAPIClient;

public static class FlowLoggerFactory
{
    static IFlowLogger defaultInstance = null;

    public static void Initialize(bool traceEnabled, string pubkey, Uri loggerUri, Func<HttpClient> httpFactory)
    {
        defaultInstance= new FlowLogger(traceEnabled, pubkey, loggerUri, httpFactory);
    }

    public static bool Enabled { get => defaultInstance.Enabled; set => defaultInstance.Enabled = value; }   

    public static LogWrapper<T> Trace<T>()
    {
        if(defaultInstance == null)
            throw new InvalidOperationException("FlowLoggerFactory not initialized");
        return new LogWrapper<T>(defaultInstance);
    }
}
