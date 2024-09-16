namespace GigDebugLoggerAPIClient;

public static class ConsoleLoggerFactory
{
    static ConsoleLogger defaultInstance = new ConsoleLogger();

    public static bool Enabled { get => defaultInstance.Enabled; set => defaultInstance.Enabled = value; }

    public static void Initialize(bool traceEnabled)
    {
        defaultInstance.Initialize(traceEnabled);
    }

    public static LogWrapper<T> Trace<T>()
    {
        return new LogWrapper<T>(defaultInstance);
    }
}
