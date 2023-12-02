using GigGossipSettler.Exceptions;
using GigGossipSettlerAPI.Models;
using System.Net;

namespace GigGossipSettlerAPI.Middlewares
{
    public class ErrorHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger _logger;
        private readonly bool _isDevelopment;

        public ErrorHandlerMiddleware(RequestDelegate next, ILogger<ErrorHandlerMiddleware> logger)
        {
            _logger = logger;
            _next = next;
            _isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        }
        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Something went wrong: {ex}");
                await HandleExceptionAsync(httpContext, ex);
            }
        }
        private async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            string devMessage =  ex.Message;
            int apiErrorCode = -1;
            int statusCode = (int)HttpStatusCode.InternalServerError;

            switch (ex)
            {
                case InvalidAuthTokenException:
                    {
                        apiErrorCode = (int)(ex as SettlerException).ErrorCode;
                        statusCode = (int)HttpStatusCode.Unauthorized;
                    break;
                    }

                case UnknownPreimageException 
                    or PropertyNotGrantedException 
                    or UnknownCertificateException:
                    {
                        apiErrorCode = (int)(ex as SettlerException).ErrorCode;
                        statusCode = (int)HttpStatusCode.BadRequest;
                        break;
                    }
                case SettlerException:
                    {
                        apiErrorCode = (int)(ex as SettlerException).ErrorCode;
                        break;
                    }
                    default: 
                    {
                        break;
                    }
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            await context.Response.WriteAsync(new ErrorDetails()
            {
                Message = _isDevelopment ? devMessage : ((HttpStatusCode)statusCode).ToString(),
                ApiErrorCode = apiErrorCode
            }.ToString());
        }
    }

}
