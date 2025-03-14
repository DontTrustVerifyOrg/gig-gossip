﻿using System;
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
    void Error(string message);
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

    public void Error(string message)
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

    ~LogEntryClosure()
    {
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

    public void Error(string message)
    {
        wrapper.TraceError(guid, message);
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
        try
        {
            if (flowLogger.Enabled)
                return new LogEntryClosure<T>(this, Guid.NewGuid(), memberName, sourceFilePath, sourceLineNumber);
            else
                return new EmptyLogEntryClosure<T>(this, Guid.Empty, memberName, sourceFilePath, sourceLineNumber);
        }
        catch
        {
            return new EmptyLogEntryClosure<T>(this, Guid.Empty, memberName, sourceFilePath, sourceLineNumber);
        }
    }

    public void TraceIn(Guid guid, string memberName, string sourceFilePath, int sourceLineNumber)
    {
        try
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
        catch {/*PASS*/}
    }



    public void TraceArgs(Guid guid, params dynamic[] objects)
    {
        try
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
        catch {/*PASS*/}
    }



    public R TraceRet<R>(Guid guid, R r)
    {
        try
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
        }
        catch {/*PASS*/}

        return r;
    }

    public void TraceOut(Guid guid)
    {
        try
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
        catch {/*PASS*/}
    }

    public R TraceIter<R>(Guid guid, R r)
    {
        try
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
        }
        catch {/*PASS*/}

        return r;
    }


    public void TraceExc(Guid guid, Exception ex)
    {
        try
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
        catch {/*PASS*/}
    }

    public void TraceInfo(Guid guid, string msg)
    {
        try
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
        catch {/*PASS*/}
    }

    public void TraceWarning(Guid guid, string msg)
    {
        try
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
        catch {/*PASS*/}
    }

    public void TraceError(Guid guid, string msg)
    {
        try
        {
            if (flowLogger.Enabled)
                flowLogger.WriteToLog(
                    System.Diagnostics.TraceEventType.Error,
                    JsonSerialize(new
                    {
                        kind = "error",
                        id = guid,
                        message = msg,
                    }));
        }
        catch {/*PASS*/}
    }

    public void TraceNewMessage(Guid guid, string a, string b, string message)
    {
        try
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
        catch {/*PASS*/}
    }

    public void TraceNewReply(Guid guid, string a, string b, string message)
    {
        try
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
        catch {/*PASS*/}
    }

    public void TraceNewConnected(Guid guid, string a, string b, string message)
    {
        try
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
        catch {/*PASS*/}
    }

    public void TraceNewNote(Guid guid, string a, string message)
    {
        try
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
        catch {/*PASS*/}
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

