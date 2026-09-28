using System.Net;
using System.Text.Json;
using CardShare.Contracts;
using CardShare.Domain;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace CardShare.Server.Middleware;

public sealed class DomainExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DomainExceptionMiddleware> _log;
    private readonly JsonSerializerOptions _json;

    public DomainExceptionMiddleware(RequestDelegate next, ILogger<DomainExceptionMiddleware> log, IOptions<JsonOptions> json)
    {
        _next = next;
        _log = log;
        // 用 ASP.NET 已配置的序列化选项（Program.cs ConfigureHttpJsonOptions），不再手建。
        _json = json.Value.SerializerOptions;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            _log.LogInformation("API {Method} {Path} -> {Status} {Code}: {Message}",
                context.Request.Method,
                context.Request.Path,
                (int)MapStatus(ex.Code),
                ex.Code,
                ex.Message);

            context.Response.StatusCode = (int)MapStatus(ex.Code);
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new ApiError
            {
                Code = ex.Code,
                Message = ex.Message
            }, _json));
        }
    }

    private static HttpStatusCode MapStatus(string code)
    {
        return code switch
        {
            ErrorCodes.Unauthorized => HttpStatusCode.Unauthorized,
            ErrorCodes.GuestDisabled => HttpStatusCode.Forbidden,
            ErrorCodes.LevelLocked => HttpStatusCode.Forbidden,
            ErrorCodes.HeroLocked => HttpStatusCode.Forbidden,
            ErrorCodes.ProviderNotConfigured => HttpStatusCode.NotImplemented,
            ErrorCodes.RunNotFound => HttpStatusCode.NotFound,
            ErrorCodes.RunAlreadySettled => HttpStatusCode.Conflict,
            ErrorCodes.ActiveRunExists => HttpStatusCode.Conflict,
            ErrorCodes.Conflict => HttpStatusCode.Conflict,
            ErrorCodes.InsufficientEnergy => HttpStatusCode.BadRequest,
            ErrorCodes.InsufficientGold => HttpStatusCode.BadRequest,
            ErrorCodes.AdLimitReached => HttpStatusCode.BadRequest,
            ErrorCodes.TalentPoolEmpty => HttpStatusCode.BadRequest,
            ErrorCodes.RunMismatch => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.BadRequest
        };
    }
}
