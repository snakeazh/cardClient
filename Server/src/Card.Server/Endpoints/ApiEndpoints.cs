namespace CardShare.Server.Endpoints;

public static class ApiEndpoints
{
    public static void MapCardApi(this WebApplication app)
    {
        app.MapHealthApi();
        var v1 = app.MapGroup("/v1");
        v1.MapAuthApi();
        v1.MapPlayerApi();
        v1.MapPveApi();
        if (app.Environment.IsDevelopment())
        {
            v1.MapDebugApi();
        }

        var adminToken = app.Configuration.GetValue<string>("Admin:Token");
        if (!string.IsNullOrWhiteSpace(adminToken))
        {
            v1.MapAdminApi(adminToken, app.Logger);
        }
    }
}
