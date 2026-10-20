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

// 静的ファイル配信
app.UseStaticFiles();

app.UseRouting();

// セッションミドルウェアの有効化
app.UseSession();

app.UseAuthorization();

// 共通ヘルパー: ブラウザ画面へのアクセス時に未ログインなら /login?returnUrl=... へ強制遷移させた上で HTML を返す
static async Task ServeProtectedHtmlPageAsync(HttpContext context, WebApplication webApp, string htmlFileName)
{
    var store = context.RequestServices.GetRequiredService<SharedLoginUserStore>();
    string? token = store.ExtractToken(context);
    var (loginUser, _) = await store.GetLoginUserAsync(token);

    if (loginUser == null)
    {
        string originalPathAndQuery = context.Request.Path + context.Request.QueryString.Value;
        string loginRedirect = $"/login?returnUrl={Uri.EscapeDataString(originalPathAndQuery)}";
        context.Response.Redirect(loginRedirect);
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(webApp.Environment.WebRootPath, htmlFileName));
}

// GET /login でブラウザおよび WinForms WebView2 用の Web ログイン画面を返す
// autoRedirectToIdpOnLogin = true の場合はログイン画面を表示せず直接 SAML/OIDC IdP リダイレクト処理へ遷移する
app.MapGet("/login", async context =>
{
    var store = context.RequestServices.GetRequiredService<SharedLoginUserStore>();
    string returnUrl = context.Request.Query["returnUrl"].ToString();
    bool clientWebView = string.Equals(context.Request.Query["clientWebView"], "true", StringComparison.OrdinalIgnoreCase);
    bool forceShowLogin = string.Equals(context.Request.Query["forceShowLogin"], "true", StringComparison.OrdinalIgnoreCase);
    bool queryAutoRedirect = string.Equals(context.Request.Query["autoRedirectToIdpOnLogin"], "true", StringComparison.OrdinalIgnoreCase);

    if (!forceShowLogin && (store.AutoRedirectToIdpOnLogin || queryAutoRedirect))
    {
        string ssoRedirectUrl = $"/api/bff/auth/sso/redirect?returnUrl={Uri.EscapeDataString(returnUrl)}&clientWebView={clientWebView.ToString().ToLowerInvariant()}";
        context.Response.Redirect(ssoRedirectUrl);
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "login.html"));
});

// GET /web/view/drawings?drw_no='drw004' および GET /legacy-drawings
// 未ログイン時は 1. ログイン画面を表示 -> 2. ログイン完了後に元の URL (/web/view/drawings?drw_no='drw004') で図面を表示
app.MapGet("/web/view/drawings", context => ServeProtectedHtmlPageAsync(context, app, "legacy-drawings.html"));
app.MapGet("/legacy-drawings", context => ServeProtectedHtmlPageAsync(context, app, "legacy-drawings.html"));

// GET /projects でモダン案件統合管理 (PRJ_TBL) Web 画面を返す（未ログイン時は /login?returnUrl=/projects へ強制遷移）
app.MapGet("/projects", context => ServeProtectedHtmlPageAsync(context, app, "projects.html"));

// GET /workflows でモダンワークフロー管理 Web 画面を返す（未ログイン時は /login?returnUrl=/workflows へ強制遷移）
app.MapGet("/workflows", context => ServeProtectedHtmlPageAsync(context, app, "workflows.html"));

app.MapControllers();

app.Run();
