using CardShare.Domain;

namespace CardShare.Server.Middleware;

public sealed class AccessTokenMiddleware
{
    private static readonly HashSet<string> Anonymous = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/v1/health",
        "/v1/auth/login",
        "/v1/auth/refresh",
        "/v1/pvp/ws"
    };

    private readonly RequestDelegate _next;

    public AccessTokenMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, ITokenService tokens)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (HttpMethods.IsOptions(context.Request.Method) || Anonymous.Contains(path))
        {
            await _next(context);
            return;
        }

        if (!path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw DomainException.Unauthorized("Missing bearer token.");
        }

        var token = header.Substring("Bearer ".Length).Trim();
        var userId = await tokens.ResolveAccessTokenAsync(token, context.RequestAborted);
        if (userId == null)
        {
            throw DomainException.Unauthorized("Invalid access token.");
        }

        context.Items[HttpContextUser.ItemKey] = userId.Value;
        await _next(context);
    }
}
