using CardShare.Contracts;
using CardShare.Domain.Pve;

namespace CardShare.Server.Endpoints;

public static class PveEndpoints
{
    public static void MapPveApi(this RouteGroupBuilder v1)
    {
        v1.MapPost("/pve/start", async (HttpContext http, PveStartRequest request, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.StartPveAsync(http.RequireUserId(), request, ct)));
        v1.MapGet("/pve/run/active", async (HttpContext http, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.GetActiveRunAsync(http.RequireUserId(), ct)));
        v1.MapPost("/pve/settle", async (HttpContext http, PveSettleRequest request, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.SettlePveAsync(http.RequireUserId(), request, ct)));
        v1.MapPost("/pve/run/progress", async (HttpContext http, PveProgressRequest request, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.ReportProgressAsync(http.RequireUserId(), request, ct)));
        v1.MapPost("/pve/shop/enter", async (HttpContext http, PveShopEnterRequest request, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.EnterShopAsync(http.RequireUserId(), request.RunId, request.FreeShopRefreshLeft, ct)));
        v1.MapPost("/pve/shop/buy", async (HttpContext http, PveShopActionRequest request, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.BuyShopRelicAsync(http.RequireUserId(), request.RunId, request.RelicId, ct)));
        v1.MapPost("/pve/shop/sell", async (HttpContext http, PveShopActionRequest request, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.SellShopRelicAsync(http.RequireUserId(), request.RunId, request.RelicId, ct)));
        v1.MapPost("/pve/shop/refresh", async (HttpContext http, PveShopActionRequest request, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.RefreshShopAsync(http.RequireUserId(), request.RunId, ct)));
        v1.MapPost("/pve/run/grant-gold", async (HttpContext http, PveRunGoldRequest request, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.GrantRunGoldAsync(http.RequireUserId(), request.RunId, request.Amount, ct)));
        v1.MapPost("/pve/run/spend", async (HttpContext http, PveRunSpendRequest request, PveRunService pve, CancellationToken ct)
            => Results.Json(await pve.SpendRunGoldAsync(http.RequireUserId(), request.RunId, request.Amount, ct)));
    }
}
