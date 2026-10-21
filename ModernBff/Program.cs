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

// 1. 分散キャッシュ・分散セッションのセットアップ (疎結合・2プロセスモード時はインメモリキャッシュを使用)
bool enableRedisSession = builder.Configuration.GetValue<bool>("StranglerFig:EnableRedisSession", false);
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";

if (enableRedisSession)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "StranglerSharedSession:";
    });

    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        try
        {
            var options = ConfigurationOptions.Parse(redisConnectionString);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 1500;
            return ConnectionMultiplexer.Connect(options);
        }
        catch
        {
            var options = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ConnectTimeout = 1000
            };
            options.EndPoints.Add("127.0.0.1", 6379);
            return ConnectionMultiplexer.Connect(options);
        }
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// 2. セッションの有効化
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".StranglerFig.SharedSession";
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 3. 認証ストア & MCP エンジン登録
builder.Services.AddSingleton<SharedLoginUserStore>();
builder.Services.AddSingleton<ModernBff.Mcp.StranglerMcpEngine>();

// 4. 内蔵 YARP リバースプロキシ (Embedded YARP) の登録
// これにより、独立した YarpGateway プロセスを起動せず、ModernBff 単体でゲートウェイ機能を提供
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

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
// ・疎結合モード (StranglerFig:EnableWebLoginPage=false): レガシーログイン画面の利用案内ページを表示
// ・通常・統合モード: モダン統合ログイン画面 (login.html) を表示
app.MapGet("/login", async context =>
{
    var config = context.RequestServices.GetRequiredService<IConfiguration>();
    bool enableWebLogin = config.GetValue<bool>("StranglerFig:EnableWebLoginPage", false);
    bool forceShowLogin = string.Equals(context.Request.Query["forceShowLogin"], "true", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(context.Request.Query["force"], "true", StringComparison.OrdinalIgnoreCase);

    // 疎結合モード時はレガシーログインの案内を表示（後から有効化・切り替え可能）
    if (!enableWebLogin && !forceShowLogin)
    {
        string legacyUrl = config["LegacyMvc:BaseUrl"] ?? "http://localhost:5001/";
        string legacyLoginUrl = $"{legacyUrl.TrimEnd('/')}/LegacyLogin.aspx";

        string noticeHtml = $@"<!DOCTYPE html>
<html lang=""ja"">
<head>
    <meta charset=""utf-8"" />
    <title>Strangler Fig - 疎結合運用モード案内</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0f172a; color: #f8fafc; padding: 40px 20px; }}
        .card {{ max-width: 680px; margin: 0 auto; background: #1e293b; border: 1px solid #334155; border-radius: 12px; padding: 28px; box-shadow: 0 10px 25px rgba(0,0,0,0.3); }}
        .badge {{ display: inline-block; background: #10b981; color: #022c22; font-weight: bold; font-size: 12px; padding: 4px 10px; border-radius: 999px; margin-bottom: 12px; }}
        h1 {{ font-size: 20px; margin-top: 0; color: #38bdf8; }}
        p {{ font-size: 14px; line-height: 1.6; color: #cbd5e1; }}
        .btn {{ display: inline-block; background: #2563eb; color: #fff; text-decoration: none; padding: 10px 18px; border-radius: 6px; font-weight: bold; margin-top: 8px; margin-right: 10px; }}
        .btn-secondary {{ background: #475569; }}
        .code-box {{ background: #090d16; border: 1px solid #1e293b; border-radius: 6px; padding: 12px; font-family: Consolas, monospace; font-size: 12.5px; color: #a5f3fc; overflow-x: auto; margin-top: 14px; }}
    </style>
</head>
<body>
    <div class=""card"">
        <span class=""badge"">✓ Strangler Fig 疎結合運用中 (Decoupled Mode)</span>
        <h1>レガシー独立運用 ＆ モダン REST API / MCP 特化</h1>
        <p>
            現在の構成方針に基づき、<strong>セッション共有 (Redis) や YarpGateway は介さず、LegacyMvc はそのまま独立稼働</strong>しています。<br />
            ブラウザ・WinForms クライアントからの画面操作は、レガシー側のログイン画面をご利用ください。
        </p>
        <div style=""margin: 18px 0;"">
            <a href=""{legacyLoginUrl}"" class=""btn"">レガシー ログイン画面へ進む (:5001)</a>
            <a href=""/login?force=true"" class=""btn btn-secondary"">旧モダン検証画面を表示 (一時検証用)</a>
        </div>
        <p style=""font-size: 13px; color: #94a3b8;"">
            ※ モダン側 (ModernBff) は AIツール / PowerShell 向けの <strong>REST API (/api/*)</strong> および <strong>MCP コントローラー (/api/bff/mcp/*)</strong> として直接稼働しています。<br />
            認証は <code>POST /oauth/token (grant_type=password)</code> による Bearer トークンをご利用ください。
        </p>
        <div class=""code-box"">
# PowerShell からの利用例 (パターンA: USR_TBL 認証)<br/>
$t = Invoke-RestMethod -Uri 'http://localhost:5002/oauth/token' -Method Post -Body @{{ grant_type='password'; user_id='yamada'; password='pass123' }}<br/>
Invoke-RestMethod -Uri 'http://localhost:5002/api/items' -Headers @{{ Authorization = ""Bearer $($t.access_token)"" }}
        </div>
    </div>
</body>
</html>";
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(noticeHtml);
        return;
    }

    var store = context.RequestServices.GetRequiredService<SharedLoginUserStore>();
    string returnUrl = context.Request.Query["returnUrl"].ToString();
    bool clientWebView = string.Equals(context.Request.Query["clientWebView"], "true", StringComparison.OrdinalIgnoreCase);
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

// 組み込み YARP リバースプロキシの有効化 (未処理のレガシーパスを LegacyMvc へ転送)
app.MapReverseProxy();

app.Run();
