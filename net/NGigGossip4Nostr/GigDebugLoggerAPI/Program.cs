using GigDebugLoggerAPI;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using TraceExColor;

TraceEx.TraceInformation("[[lime]]Starting[[/]] ...");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
});
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
//app.UseMiddleware<ErrorHandlerMiddleware>();
app.UseStatusCodePages();
app.UseHsts();


IConfigurationRoot GetConfigurationRoot(string defaultFolder, string iniName)
{
    var basePath = Environment.GetEnvironmentVariable("GIGGOSSIP_BASEDIR");
    if (basePath == null)
        basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), defaultFolder);
    foreach (var arg in args)
        if (arg.StartsWith("--basedir"))
            basePath = arg.Substring(arg.IndexOf('=') + 1).Trim().Replace("\"", "").Replace("\'", "");
        else if (arg.StartsWith("--cfg"))
            iniName = arg.Substring(arg.IndexOf('=') + 1).Trim().Replace("\"", "").Replace("\'", "");

    var builder = new ConfigurationBuilder();
    builder.SetBasePath(basePath)
           .AddIniFile(iniName)
           .AddEnvironmentVariables()
           .AddCommandLine(args);

    return builder.Build();
}

var config = GetConfigurationRoot(".giggossip", "giglog.conf");
var loggerSettings = config.GetSection("logger").Get<LoggerSettings>();

Singlethon.LogFolder = loggerSettings.LogFolder.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

TraceEx.TraceInformation("... Running");

app.MapGet("/gettoken", (string apikey) =>
{
    try
    {
        return new Result<Guid>(Guid.NewGuid());
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result<Guid>(ex);
    }
})
.WithName("GetToken")
.WithSummary("Creates authorisation token guid")
.WithDescription("Creates a new token Guid that is used for further communication with the API")
.WithOpenApi(g =>
{
    g.Parameters[0].Description = "public key identifies the API user";
    return g;
});

app.MapPost("/logevent/{apikey}/{pubkey}/{eventType}", async (HttpRequest request, string apikey, string pubkey, string eventType, [FromForm] IFormFile message, [FromForm] IFormFile exception)
    =>
{
    try
    {
        Singlethon.SystemLogEvent(pubkey, Enum.Parse<System.Diagnostics.TraceEventType>(eventType), Encoding.UTF8.GetString(await message.ToBytes()), Encoding.UTF8.GetString(await exception.ToBytes()));
        return new Result();
    }
    catch (Exception ex)
    {
        TraceEx.TraceException(ex);
        return new Result(ex);
    }
})
.WithName("LogEvent")
.WithSummary("Logs an event")
.WithDescription("Logs an event");

app.Run(loggerSettings.ListenHost.AbsoluteUri);

[Serializable]
public class SystemLogEntry
{
    public required Guid EntryId { get; set; }
    public required string PublicKey { get; set; }
    public required long Timestamp { get; set; }
    public required TraceEventType EventType { get; set; }
    public required string Message { get; set; }
    public required string Exception { get; set; }
}


[Serializable]
public struct Result
{
    public Result() { }
    public Result(Exception exception)
    {
        ErrorCode = LoggerErrorCode.OperationFailed;
        if (exception is LoggerException ex)
            ErrorCode = ex.ErrorCode;
        ErrorMessage = exception.Message;
    }
    public LoggerErrorCode ErrorCode { get; set; } = LoggerErrorCode.Ok;
    public string ErrorMessage { get; set; } = "";
}

[Serializable]
public struct Result<T>
{
    public Result(T value) { Value = value; }
    public Result(Exception exception)
    {
        ErrorCode = LoggerErrorCode.OperationFailed;
        if (exception is LoggerException ex)
            ErrorCode = ex.ErrorCode;
        ErrorMessage = exception.Message;
    }
    public T? Value { get; set; } = default;
    public LoggerErrorCode ErrorCode { get; set; } = LoggerErrorCode.Ok;
    public string ErrorMessage { get; set; } = "";
}

public class LoggerSettings
{
    public required Uri ListenHost { get; set; }
    public required Uri ServiceUri { get; set; }
    public required string LogFolder { get; set; }
}
