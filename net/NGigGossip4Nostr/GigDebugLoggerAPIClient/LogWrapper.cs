using System;
using Newtonsoft.Json;

namespace GigDebugLoggerAPIClient;


public interface ILogEntryClosure : IDisposable
{
    ILogEntryClosure Args(params dynamic[] objects);
    R Ret<R>(R r);
    void Exception(Exception ex);
    void Iteration<I>(I v);
    void Info(string message);
    void Warning(string message);
    void NewMessage(string a, string b, string message);
    void NewReply(string a, string b, string message);
    void NewConnected(string a, string b, string message);
    void NewNote(string a, string message);
}

public class EmptyLogEntryClosure<T> : ILogEntryClosure
{
    public EmptyLogEntryClosure(LogWrapper<T> wrapper, Guid guid, string memberName, string sourceFilePath, int lineno)
    {
    }

    public ILogEntryClosure Args(params dynamic[] objects)
    {
        return this;
    }

    public R Ret<R>(R r)
    {
        return r;
    }

    public void Dispose()
    {
    }

    public void Exception(Exception ex)
    {
    }

    public void Iteration<I>(I v)
    {
    }

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void NewMessage(string a, string b, string message)
    {
    }

    public void NewReply(string a, string b, string message)
    {
    }

    public void NewConnected(string a, string b, string message)
    {
    }

    public void NewNote(string a, string message)
    {
    }
}


public class LogEntryClosure<T> : ILogEntryClosure
{
    LogWrapper<T> wrapper;
    Guid guid;

    public LogEntryClosure(LogWrapper<T> wrapper, Guid guid, string memberName, string sourceFilePath, int lineno)
    {
        this.wrapper = wrapper;
        this.guid = guid;
        wrapper.TraceIn(guid, memberName, sourceFilePath, lineno);
    }

    public ILogEntryClosure Args(params dynamic[] objects)
    {
        wrapper.TraceArgs(guid, objects);
        return this;
    }

    public R Ret<R>(R r)
    {
        return wrapper.TraceRet(guid, r);
    }

    public void Dispose()
    {
        wrapper.TraceOut(guid);
    }

    public void Exception(Exception ex)
    {
        wrapper.TraceExc(guid, ex);
    }

    public void Iteration<I>(I v)
    {
        wrapper.TraceIter(guid, v);
    }

    public void Info(string message)
    {
        wrapper.TraceInfo(guid, message);
    }

    public void Warning(string message)
    {
        wrapper.TraceWarning(guid, message);
    }

    public void NewMessage(string a, string b, string message)
    {
        wrapper.TraceNewMessage(guid, a, b, message);
    }

    public void NewReply(string a, string b, string message)
    {
        wrapper.TraceNewReply(guid, a, b, message);
    }

    public void NewConnected(string a, string b, string message)
    {
        wrapper.TraceNewConnected(guid, a, b, message);
    }

    public void NewNote(string a, string message)
    {
        wrapper.TraceNewNote(guid, a,  message);
    }


}

public class LogWrapper<T>
{
    protected string typeFullName;
    protected IFlowLogger flowLogger;

    public LogWrapper(IFlowLogger flowLogger)
    {
        this.typeFullName = typeof(T).FullName;
        this.flowLogger = flowLogger;
    }

    public ILogEntryClosure Log(
        [System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0)
    {
        if (flowLogger.Enabled)
            return new LogEntryClosure<T>(this, Guid.NewGuid(), memberName, sourceFilePath, sourceLineNumber);
        else
            return new EmptyLogEntryClosure<T>(this, Guid.Empty, memberName, sourceFilePath, sourceLineNumber);
    }

    public void TraceIn(Guid guid, string memberName, string sourceFilePath, int sourceLineNumber)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Information,
                JsonSerialize(new
                {
                    kind = "call",
                    id = guid,
                    file = Path.GetFileName(sourceFilePath),
                    method = memberName,
                    line = sourceLineNumber,
                    type = typeFullName
                }));
    }



    public void TraceArgs(Guid guid, params dynamic[] objects)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Information,
                JsonSerialize(new
                {
                    kind = "args",
                    id = guid,
                    args = (from o in objects select SerializableToJson(o)).ToArray(),
                }));
    }



    public R TraceRet<R>(Guid guid, R r)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Information,
                JsonSerialize(new
                {
                    kind = "retval",
                    id = guid,
                    value = SerializableToJson(r)
                }));
        return r;
    }

    public void TraceOut(Guid guid)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Information,
                JsonSerialize(new
                {
                    kind = "return",
                    id = guid,
                }));
    }

    public R TraceIter<R>(Guid guid, R r)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Information,
                JsonSerialize(new
                {
                    kind = "iteration",
                    id = guid,
                    value = SerializableToJson(r)
                }));
        return r;
    }


    public void TraceExc(Guid guid, Exception ex)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Critical,
                JsonSerialize(new
                {
                    kind = "exception",
                    id = guid,
                    exception = SerializableException(ex),
                }));
    }

    public void TraceInfo(Guid guid, string msg)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Information,
                JsonSerialize(new
                {
                    kind = "info",
                    id = guid,
                    message = msg,
                }));
    }

    public void TraceWarning(Guid guid, string msg)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Warning,
                JsonSerialize(new
                {
                    kind = "warning",
                    id = guid,
                    message = msg,
                }));
    }

    public void TraceNewMessage(Guid guid, string a, string b, string message)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Transfer,
                JsonSerialize(new
                {
                    kind = "message",
                    id = guid,
                    message = "\t" + a + "->>" + b + ": " + message,
                }));
    }

    public void TraceNewReply(Guid guid, string a, string b, string message)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Transfer,
                JsonSerialize(new
                {
                    kind = "reply",
                    id = guid,
                    message = "\t" + a + "-->>" + b + ": " + message,
                }));
    }

    public void TraceNewConnected(Guid guid, string a, string b, string message)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Transfer,
                JsonSerialize(new
                {
                    kind = "connected",
                    id = guid,
                    message = "\t" + a + "--)" + b + ": " + message,
                }));
    }

    public void TraceNewNote(Guid guid, string a, string message)
    {
        if (flowLogger.Enabled)
            flowLogger.WriteToLog(
                System.Diagnostics.TraceEventType.Transfer,
                JsonSerialize(new
                {
                    kind = "note",
                    id = guid,
                    message = "\t Note over " + a + ": " + message,
                }));
    }

    public static object SerializableToJson(object obj)
    {
        try
        {
            if (obj == null)
                return "(null)";
            else if (obj is byte[])
                return Convert.ToBase64String((byte[])obj);
            else
                Newtonsoft.Json.JsonConvert.SerializeObject(obj);
            return obj;
        }
        catch (Exception)
        {
            return obj.ToString();
        }
    }

    public object SerializableException(Exception ex)
    {
        return new
        {
            message = ex.Message,
            stack = ex.StackTrace,
            type = ex.GetType().FullName,
            innerexception = ex.InnerException != null ? SerializableException(ex.InnerException) : "(null)"
        };
    }


    public static string JsonSerialize(object o)
    {
        return Newtonsoft.Json.JsonConvert.SerializeObject(o, new JsonSerializerSettings
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        });
    }
}

