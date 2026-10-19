using ModernBff.Data;
using ModernBff.MDL.Data;
using ModernBff.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Windows サービスとしてインストール・起動された場合に対応
builder.Host.UseWindowsService();

builder.Services.AddControllers();

// 0. EF Core DbContext の登録 (appsettings.json の Database:Provider で Oracle / Postgres / SqlServer / InMemory を切り替え)
builder.Services.AddDbContext<StranglerDbContext>(options =>
{
    DatabaseConfigurationExtensions.ConfigureStranglerDatabase(
        options,
        builder.Configuration,
        out _,
        out _);
});

// 1. AddStackExchangeRedisCache による分散キャッシュ・分散セッションのセットアップ
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
    options.InstanceName = "StranglerSharedSession:";
});

// 2. 分散セッションの有効化
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".StranglerFig.SharedSession";
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 3. LegacyMvc と直接 Redis KVS を共有するための IConnectionMultiplexer & SharedLoginUserStore 登録
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<SharedLoginUserStore>();
builder.Services.AddSingleton<ModernBff.Mcp.StranglerMcpEngine>();

// 4. レガシー MVC を内部呼び出しするための HttpClient 登録
builder.Services.AddHttpClient("LegacyMvcClient", client =>
{
    var legacyBaseUrl = builder.Configuration["LegacyMvc:BaseUrl"] ?? "http://localhost:5001/";
    client.BaseAddress = new Uri(legacyBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

// 静的ファイル配信 (wwwroot/login.html など)
app.UseStaticFiles();

app.UseRouting();

// セッションミドルウェアの有効化
app.UseSession();

app.UseAuthorization();

// GET /login でブラウザおよび WinForms WebView2 用の Web ログイン画面を返す
app.MapGet("/login", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "login.html"));
});

// GET /legacy-drawings でレガシー図面 (DRW_TBL) 管理 Web 画面を返す
app.MapGet("/legacy-drawings", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "legacy-drawings.html"));
});

// GET /projects でモダン案件統合管理 (PRJ_TBL: 品目・図面関連付け) Web 画面を返す
app.MapGet("/projects", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "projects.html"));
});

// GET /workflows でモダンワークフロー管理 (申請 -> 照査 -> 承認) Web 画面を返す
app.MapGet("/workflows", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "workflows.html"));
});

app.MapControllers();

app.Run();
