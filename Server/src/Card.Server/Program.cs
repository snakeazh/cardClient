using CardShare.Infrastructure;
using CardShare.Infrastructure.Postgres;
using CardShare.Server.Endpoints;
using CardShare.Server.Middleware;
using CardShare.Server.Pvp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCardInfrastructure(builder.Configuration);
builder.Services.AddSingleton<PvpConnectionHub>();
builder.Services.AddSingleton<PvpBattleHost>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();
app.UseWebSockets();
app.UseMiddleware<DomainExceptionMiddleware>();
app.UseMiddleware<AccessTokenMiddleware>();
app.MapCardApi();
app.MapPvpWebSocket();

if (string.Equals(app.Configuration["Persistence:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CardDbContext>();
    db.Database.EnsureCreated();
}

app.Run();
