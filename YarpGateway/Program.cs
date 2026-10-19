var builder = WebApplication.CreateBuilder(args);

// Windows サービスとしてインストール・起動された場合に対応
builder.Host.UseWindowsService();

// YARP (Microsoft.ReverseProxy) の登録と appsettings.json からの設定読み込み
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Service = "YarpGateway",
    Status = "Running",
    Routes = new[]
    {
        "/legacy-api/{**catch-all} -> LegacyMvc",
        "/api/bff/{**catch-all} -> ModernBff"
    }
}));

// リバースプロキシエンドポイントのマッピング
app.MapReverseProxy();

app.Run();
