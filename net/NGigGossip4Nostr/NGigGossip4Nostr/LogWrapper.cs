using System;
using GigLNDWalletAPIClient;
using System.Runtime.CompilerServices;
using GoogleApi.Entities.Maps.DistanceMatrix.Response;
using System.Collections.Generic;
using GoogleApi.Interfaces;

namespace NGigGossip4Nostr;

public static class FlowLoggerExtension
{
    public static string MetNam(this IFlowLogger flowLogger, [CallerMemberName] string memberName = "")
    {
        return memberName;
    }

    public static async Task TraceInAsync<T>(this IFlowLogger flowLogger, T api, Guid? guid, string? memberName, params dynamic[] objects)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "call",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                args = objects
            }));
    }

    public static async Task TraceWarningAsync<T>(this IFlowLogger flowLogger, T api, Guid? guid, string? memberName, string msg)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "call",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                message = msg
            }));
    }

    public static async Task<R> TraceOutAsync<T, R>(this IFlowLogger flowLogger, T api, Guid? guid, string? memberName, R r)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "return",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                retval = r
            }));
        return r;
    }

    public static async Task<R> TraceIterAsync<T, R>(this IFlowLogger flowLogger, T api, Guid? guid, string? memberName, R r)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "iteration",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                retval = r
            }));
        return r;
    }


    public static async Task TraceVoidAsync<T>(this IFlowLogger flowLogger, T api, Guid? guid, string? memberName)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "return",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
            }));
    }

    public static async Task TraceExcAsync<T>(this IFlowLogger flowLogger, T api, Guid? guid, string? memberName, Exception ex)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceExceptionAsync(ex, Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "exception",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                exception = ex.Message,
            }));
    }
}

public class LogWrapper<T>
{
    protected T api;
    protected IFlowLogger flowLogger;

    public LogWrapper(IFlowLogger flowLogger, T api)
    {
        this.api = api;
        this.flowLogger = flowLogger;
    }

    public string MetNam([CallerMemberName] string memberName = "")
    {
        return memberName;
    }

    public async Task TraceInAsync(Guid? guid, string? memberName, params dynamic[] objects)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "call",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                args = objects
            }));
    }

    public async Task<R> TraceOutAsync<R>(Guid? guid, string? memberName, R r)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "return",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                retval = r
            }));
        return r;
    }

    public async Task<R> TraceIterAsync<R>(Guid? guid, string? memberName, R r)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "iteration",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                retval = r
            }));
        return r;
    }


    public async Task TraceVoidAsync(Guid? guid, string? memberName)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceInformationAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "return",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
            }));
    }

    public async Task TraceExcAsync(Guid? guid, string? memberName, Exception ex)
    {
        if (flowLogger.Enabled)
            await flowLogger.TraceExceptionAsync(ex, Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                kind = "exception",
                id = guid,
                method = memberName,
                type = api.GetType().FullName,
                exception = ex.Message,
            }));
    }

}

