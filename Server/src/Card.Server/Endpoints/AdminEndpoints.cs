using System.Security.Cryptography;
using System.Text;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Admin;
using CardShare.Domain.Services;

namespace CardShare.Server.Endpoints;

/// <summary>
/// 后台 GM 命令。鉴权与玩家 Token 完全分离：请求头 <see cref="TokenHeader"/> 与配置
/// Admin:Token 做定长比较（AccessTokenMiddleware 已豁免 /v1/admin 前缀）。
/// 仅当 Admin:Token 配置非空时才挂载，生产环境不配置即不存在该组端点。
/// 每条命令执行写 admin_logs 审计记录（含失败）。
/// </summary>
public static class AdminEndpoints
{
    public const string TokenHeader = "X-Admin-Token";

    public static void MapAdminApi(this RouteGroupBuilder v1, string adminToken, ILogger logger)
    {
        var admin = v1.MapGroup("/admin");

        // 单密码登录：校验通过返回管理 token，前端存起来放 X-Admin-Token 用。
        admin.MapPost("/login", (AdminLoginRequest request) =>
        {
            if (!MatchesToken(request.Password, adminToken))
            {
                throw DomainException.Unauthorized("Invalid admin password.");
            }

            return Results.Json(new AdminLoginResponse { Token = adminToken });
        });

        admin.MapPost("/grant-gold", (HttpContext http, AdminGrantRequest request, PlayerMetaService meta, IAdminLogRepository logs, CancellationToken ct)
            => Run(http, adminToken, logs, logger, "grant-gold", request.UserId, $"amount={request.Amount}",
                uid => meta.AdminGrantGoldAsync(uid, request.Amount, ct), ct));

        admin.MapPost("/grant-energy", (HttpContext http, AdminGrantRequest request, PlayerMetaService meta, IAdminLogRepository logs, CancellationToken ct)
            => Run(http, adminToken, logs, logger, "grant-energy", request.UserId, $"amount={request.Amount}",
                uid => meta.AdminGrantEnergyAsync(uid, request.Amount, ct), ct));

        admin.MapPost("/unlock-all-heroes", (HttpContext http, AdminTargetRequest request, PlayerMetaService meta, IAdminLogRepository logs, CancellationToken ct)
            => Run(http, adminToken, logs, logger, "unlock-all-heroes", request.UserId, string.Empty,
                uid => meta.AdminUnlockAllHeroesAsync(uid, ct), ct));

        admin.MapPost("/unlock-all-talents", (HttpContext http, AdminTargetRequest request, PlayerMetaService meta, IAdminLogRepository logs, CancellationToken ct)
            => Run(http, adminToken, logs, logger, "unlock-all-talents", request.UserId, string.Empty,
                uid => meta.AdminUnlockAllTalentsAsync(uid, ct), ct));

        admin.MapPost("/unlock-all-levels", (HttpContext http, AdminTargetRequest request, PlayerMetaService meta, IAdminLogRepository logs, CancellationToken ct)
            => Run(http, adminToken, logs, logger, "unlock-all-levels", request.UserId, string.Empty,
                uid => meta.AdminUnlockAllLevelsAsync(uid, ct), ct));

        admin.MapGet("/players", async (HttpContext http, string? keyword, int? take, IPlayerRepository players, CancellationToken ct) =>
        {
            RequireAdmin(http, adminToken);
            return Results.Json(await players.SearchAsync(keyword ?? string.Empty, take ?? 20, ct));
        });

        admin.MapGet("/player", async (HttpContext http, string userId, PlayerMetaService meta, CancellationToken ct) =>
        {
            RequireAdmin(http, adminToken);
            return Results.Json(await meta.GetProfileAsync(ParseUserId(userId), ct));
        });

        admin.MapGet("/logs", async (HttpContext http, int? take, IAdminLogRepository logs, CancellationToken ct) =>
        {
            RequireAdmin(http, adminToken);
            return Results.Json(await logs.ListAsync(take ?? 50, ct));
        });
    }

    private static async Task<IResult> Run(HttpContext http, string adminToken, IAdminLogRepository logs, ILogger logger,
        string command, string userIdText, string detail, Func<Guid, Task<PlayerProfileDto>> action, CancellationToken ct)
    {
        RequireAdmin(http, adminToken);
        var targetUserId = ParseUserId(userIdText);
        var remoteIp = http.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        logger.LogInformation("Admin command {Command} target={TargetUserId} from={RemoteIp}", command, targetUserId, remoteIp);
        try
        {
            var result = await action(targetUserId);
            await TryLog(logs, logger, new AdminLogEntry
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Command = command,
                TargetUserId = targetUserId,
                Detail = detail,
                RemoteIp = remoteIp,
                Succeeded = true
            }, ct);
            return Results.Json(result);
        }
        catch (Exception ex)
        {
            await TryLog(logs, logger, new AdminLogEntry
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Command = command,
                TargetUserId = targetUserId,
                Detail = string.IsNullOrEmpty(detail) ? ex.Message : detail + " error=" + ex.Message,
                RemoteIp = remoteIp,
                Succeeded = false
            }, CancellationToken.None);
            throw;
        }
    }

    private static Guid ParseUserId(string userIdText)
    {
        if (!Guid.TryParse(userIdText, out var userId))
        {
            throw DomainException.Invalid("userId is not a valid GUID.");
        }

        return userId;
    }

    /// <summary>审计落库失败只记日志，不影响命令本身的结果。</summary>
    private static async Task TryLog(IAdminLogRepository logs, ILogger logger, AdminLogEntry entry, CancellationToken ct)
    {
        try
        {
            await logs.AddAsync(entry, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin log write failed: {Command} target={TargetUserId}", entry.Command, entry.TargetUserId);
        }
    }

    private static void RequireAdmin(HttpContext http, string adminToken)
    {
        var actual = http.Request.Headers[TokenHeader].ToString();
        if (!MatchesToken(actual, adminToken))
        {
            throw DomainException.Unauthorized("Invalid admin token.");
        }
    }

    private static bool MatchesToken(string actual, string adminToken)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(adminToken);
        var actualBytes = Encoding.UTF8.GetBytes(actual ?? string.Empty);
        return actualBytes.Length == expectedBytes.Length
               && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
