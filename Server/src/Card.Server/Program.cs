using CardShare.Infrastructure;
using CardShare.Server.Composition;
using CardShare.Server.Endpoints;
using CardShare.Server.Hosting;
using CardShare.Server.Middleware;
using CardShare.Server.Pvp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCardInfrastructure(builder.Configuration);
builder.Services.AddCardApplication(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();
// 应用只监听回环、只经 Nginx，信任 X-Forwarded-For：审计日志记录真实来源 IP。
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
});
app.UseWebSockets();
app.UseMiddleware<DomainExceptionMiddleware>();
app.UseMiddleware<AccessTokenMiddleware>();
app.MapCardApi();
app.MapPvpWebSocket();

await BackendStartup.EnsureAsync(app);

app.Run();
