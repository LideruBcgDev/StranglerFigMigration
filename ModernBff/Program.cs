using ModernBff.Data;
using ModernBff.MDL.Data;
using ModernBff.Services;

var builder = WebApplication.CreateBuilder(args);

// Windows サービスとしてインストール・起動された場合に対応
builder.Host.UseWindowsService();

builder.Services.AddControllers();

// 0. EF Core DbContext の登録 (SqlServer / InMemory 等)
builder.Services.AddDbContext<StranglerDbContext>(options =>
{
    DatabaseConfigurationExtensions.ConfigureStranglerDatabase(
        options,
        builder.Configuration,
        out _,
        out _);
});

// 1. キャッシュのセットアップ (Redis完全廃止: インメモリ分散キャッシュを使用)
builder.Services.AddDistributedMemoryCache();

// 2. セッションの有効化 (インメモリ)
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".StranglerFig.Session";
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 3. 認証ストア & MCP エンジン登録
builder.Services.AddSingleton<SharedLoginUserStore>();
builder.Services.AddSingleton<ModernBff.Mcp.StranglerMcpEngine>();

// 4. 内蔵 YARP リバースプロキシ (Embedded YARP) の登録
// 自前 API (/api/*, /oauth/*, /Client/* 等) 以外の要求は自動的に LegacyMvc (:5001) へ透過転送
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 5. レガシー MVC 内部呼び出し用 HttpClient
builder.Services.AddHttpClient("LegacyMvcClient", client =>
{
    var legacyBaseUrl = builder.Configuration["LegacyMvc:BaseUrl"] ?? "http://localhost:5001/";
    client.BaseAddress = new Uri(legacyBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

// コントローラー (OAuthController, RestApiController, ClientController, McpController 等)
app.MapControllers();

// 組み込み YARP リバースプロキシ (未処理のレガシー要求を LegacyMvc :5001 へ転送)
app.MapReverseProxy();

app.Run();
