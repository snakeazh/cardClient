using CardShare.Contracts;
using CardShare.Domain.Services;

namespace CardShare.Server.Endpoints;

public static class ApiEndpoints
{
    public static void MapCardApi(this WebApplication app)
    {
        app.MapGet("/v1/health", () => Results.Json(new { status = "ok" }));

        var v1 = app.MapGroup("/v1");
        v1.MapPost("/auth/login", async (LoginRequest request, AuthService auth, CancellationToken ct)
            => Results.Json(await auth.LoginAsync(request, ct)));
        v1.MapPost("/auth/refresh", async (RefreshTokenRequest request, AuthService auth, CancellationToken ct)
            => Results.Json(await auth.RefreshAsync(request.RefreshToken, ct)));
        v1.MapGet("/player/profile", async (HttpContext http, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.GetProfileAsync(http.RequireUserId(), ct)));
        v1.MapPost("/pve/start", async (HttpContext http, PveStartRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.StartPveAsync(http.RequireUserId(), request, ct)));
        v1.MapPost("/pve/settle", async (HttpContext http, PveSettleRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.SettlePveAsync(http.RequireUserId(), request, ct)));
        v1.MapPost("/talent/draw", async (HttpContext http, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.DrawTalentAsync(http.RequireUserId(), ct)));
        v1.MapPost("/talent/grant-ad", async (HttpContext http, TalentGrantAdRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.GrantTalentByAdAsync(http.RequireUserId(), request.TalentId, ct)));
        v1.MapPost("/energy/refill-ad", async (HttpContext http, AdProofRequest _, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.RefillEnergyByAdAsync(http.RequireUserId(), ct)));
        v1.MapPost("/adshop/claim", async (HttpContext http, AdShopClaimRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.ClaimAdShopAsync(http.RequireUserId(), request.Kind, ct)));
        v1.MapPost("/bag/grant", async (HttpContext http, BagMutateRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.GrantBagAsync(http.RequireUserId(), request.ItemId, request.Amount, ct)));
        v1.MapPost("/bag/consume", async (HttpContext http, BagMutateRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.ConsumeBagAsync(http.RequireUserId(), request.ItemId, request.Amount, ct)));
        v1.MapPost("/guide/complete", async (HttpContext http, GuideCompleteRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.CompleteGuideAsync(http.RequireUserId(), request.GroupId, ct)));
        v1.MapPost("/pve/shop/enter", async (HttpContext http, PveShopEnterRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.EnterShopAsync(http.RequireUserId(), request.RunId, request.FreeShopRefreshLeft, ct)));
        v1.MapPost("/pve/shop/buy", async (HttpContext http, PveShopActionRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.BuyShopRelicAsync(http.RequireUserId(), request.RunId, request.RelicId, ct)));
        v1.MapPost("/pve/shop/sell", async (HttpContext http, PveShopActionRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.SellShopRelicAsync(http.RequireUserId(), request.RunId, request.RelicId, ct)));
        v1.MapPost("/pve/shop/refresh", async (HttpContext http, PveShopActionRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.RefreshShopAsync(http.RequireUserId(), request.RunId, ct)));
        v1.MapPost("/pve/run/grant-gold", async (HttpContext http, PveRunGoldRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.GrantRunGoldAsync(http.RequireUserId(), request.RunId, request.Amount, ct)));
        v1.MapPost("/pve/run/spend", async (HttpContext http, PveRunSpendRequest request, PlayerCommandService commands, CancellationToken ct)
            => Results.Json(await commands.SpendRunGoldAsync(http.RequireUserId(), request.RunId, request.Amount, ct)));
        if (app.Environment.IsDevelopment())
        {
            v1.MapPost("/debug/grant-gold", async (HttpContext http, DebugGrantGoldRequest request, PlayerCommandService commands, CancellationToken ct)
                => Results.Json(await commands.DebugGrantGoldAsync(http.RequireUserId(), request.Amount, ct)));
        }
    }
}
