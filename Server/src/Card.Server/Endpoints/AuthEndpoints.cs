using CardShare.Contracts;
using CardShare.Domain.Services;

namespace CardShare.Server.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthApi(this RouteGroupBuilder v1)
    {
        v1.MapPost("/auth/login", async (LoginRequest request, AuthService auth, CancellationToken ct)
            => Results.Json(await auth.LoginAsync(request, ct)));
    }
}
