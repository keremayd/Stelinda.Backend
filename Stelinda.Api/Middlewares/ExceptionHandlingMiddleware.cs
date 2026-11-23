using System.Runtime.ExceptionServices;
using Common;
using Common.Enums;
using Common.Exceptions;
using Newtonsoft.Json;

namespace Stelinda.Api.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// Beklenmeyen hatanın iletisi yalnız geliştirmede yanıta yazılır; yayında
    /// iç ayrıntı (bağlantı, sorgu, dosya yolu) dışarı sızmasın. Ayrıntı
    /// sunucu kaydında kalır.
    /// </summary>
    private readonly bool _showDetails;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _showDetails = environment.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Bir sonraki middleware'e geçiş yap
            await _next(context);
        }
        catch (Exception ex)
        {
            // Hata meydana gelirse buraya düşer
            var edInfo = ExceptionDispatchInfo.Capture(ex);
            await HandleExceptionAsync(context, ex, edInfo);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception, ExceptionDispatchInfo edInfo)
    {
        try
        {
            StelindaException? stelindaException = edInfo.SourceException as StelindaException;
            if (stelindaException is null)
            {
                _logger.LogError(exception, "Unhandled exception");

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = 500;

                await context.Response.WriteAsync(JsonConvert.SerializeObject(new Dictionary<string, object?>
                {
                    ["data"] = null,
                    ["code"] = "500",
                    ["errorCode"] = "UnhandledException",
                    ["message"] = _showDetails ? exception.Message : "Beklenmeyen bir hata oluştu.",
                    ["detailedMessage"] = _showDetails ? exception.InnerException?.Message : null,
                }));

                return;
            }

            var errorList = new
            {
                errorCode = stelindaException.ErrorList?.Select(x => x.ErrorCode).ToList(),
                errorDescription = stelindaException.ErrorList?.Select(x => x.ErrorDescription).ToList()
            };
            
            var error = new Dictionary<string, object?>
            {
                ["data"] = stelindaException.ErrorData,
                ["code"] = stelindaException.ErrorMessage?.ErrorCode.ToString(),
                ["errorCode"] = stelindaException.ErrorMessage?.Code,
                ["errorList"] = errorList,
                ["message"] = stelindaException.ErrorMessage?.Message,
                ["detailedMessage"] = _showDetails ? stelindaException.InnerException?.Message : null,
            };
            
            int httpStatus = stelindaException.HttpStatusCode != null
                ? (int) stelindaException.HttpStatusCode
                : (!context.Response.HasStarted && context.Response.StatusCode == 200
                    ? 500
                    : context.Response.StatusCode);
            
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = httpStatus;
            
            await context.Response.WriteAsync(JsonConvert.SerializeObject(error));
            
            return;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        
        edInfo.Throw(); // Re-throw the original if we couldn't handle it
    }
}
