using CardShare.Contracts;
using CardShare.Domain.Services;

namespace CardShare.Server.Endpoints;

public static class PlayerEndpoints
{
    public static void MapPlayerApi(this RouteGroupBuilder v1)
    {
        v1.MapGet("/player/profile", async (HttpContext http, PlayerMetaService meta, CancellationToken ct)
            => Results.Json(await meta.GetProfileAsync(http.RequireUserId(), ct)));
        v1.MapPost("/talent/draw", async (HttpContext http, PlayerMetaService meta, CancellationToken ct)
            => Results.Json(await meta.DrawTalentAsync(http.RequireUserId(), ct)));
        v1.MapPost("/energy/refill-ad", async (HttpContext http, AdProofRequest _, PlayerMetaService meta, CancellationToken ct)
            => Results.Json(await meta.RefillEnergyByAdAsync(http.RequireUserId(), ct)));
        v1.MapPost("/adshop/claim", async (HttpContext http, AdShopClaimRequest request, PlayerMetaService meta, CancellationToken ct)
            => Results.Json(await meta.ClaimAdShopAsync(http.RequireUserId(), request.Kind, ct)));
        v1.MapPost("/bag/grant", async (HttpContext http, BagMutateRequest request, PlayerMetaService meta, CancellationToken ct)
            => Results.Json(await meta.GrantBagAsync(http.RequireUserId(), request.ItemId, request.Amount, ct)));
        v1.MapPost("/bag/consume", async (HttpContext http, BagMutateRequest request, PlayerMetaService meta, CancellationToken ct)
            => Results.Json(await meta.ConsumeBagAsync(http.RequireUserId(), request.ItemId, request.Amount, ct)));
        v1.MapPost("/guide/complete", async (HttpContext http, GuideCompleteRequest request, PlayerMetaService meta, CancellationToken ct)
            => Results.Json(await meta.CompleteGuideAsync(http.RequireUserId(), request.GroupId, ct)));
    }
}
