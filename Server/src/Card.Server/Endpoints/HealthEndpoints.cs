using CardShare.Server.Hosting;

namespace CardShare.Server.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthApi(this WebApplication app)
    {
        app.MapGet("/v1/health", ProbeHealthAsync);
    }

    private static async Task<IResult> ProbeHealthAsync(HttpContext http, CancellationToken ct)
    {
        var config = http.RequestServices.GetRequiredService<IConfiguration>();
        var status = await BackendHealth.ProbeAsync(http.RequestServices, config, ct);
        return Results.Json(
            new
            {
                status = status.Ok ? "ok" : "degraded",
                persistence = status.Persistence,
                postgres = status.Postgres,
                redis = status.Redis
            },
            statusCode: status.Ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
}
