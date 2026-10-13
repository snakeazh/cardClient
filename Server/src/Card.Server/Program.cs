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
app.UseWebSockets();
app.UseMiddleware<DomainExceptionMiddleware>();
app.UseMiddleware<AccessTokenMiddleware>();
app.MapCardApi();
app.MapPvpWebSocket();

await BackendStartup.EnsureAsync(app);

app.Run();
