using CardShare.Contracts;
using CardShare.Domain.Services;

namespace CardShare.Server.Endpoints;

public static class DebugEndpoints
{
    public static void MapDebugApi(this RouteGroupBuilder v1)
    {
        v1.MapPost("/debug/grant-gold", async (HttpContext http, DebugGrantGoldRequest request, PlayerMetaService meta, CancellationToken ct)
            => Results.Json(await meta.DebugGrantGoldAsync(http.RequireUserId(), request.Amount, ct)));
    }
}
