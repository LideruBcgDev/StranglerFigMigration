using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ModernBff.Data;
using ModernBff.MDL.Data;
using ModernBff.Services;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Windows サービス対応
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

// 1. キャッシュのセットアップ (インメモリ分散キャッシュ)
builder.Services.AddDistributedMemoryCache();

// 2. セッションの有効化 (インメモリ)
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".StranglerFig.Session";
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 3. 認証ストア & MCP エンジン登録
builder.Services.AddSingleton<SharedLoginUserStore>();
builder.Services.AddSingleton<ModernBff.Mcp.StranglerMcpEngine>();

// 4. 内蔵 YARP リバースプロキシ (Embedded YARP)
// 認証済みリクエストを LegacyMvc (:5001) へ転送する際、ログインユーザー情報をヘッダー注入
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformBuilderContext =>
    {
        transformBuilderContext.AddRequestTransform(async transformContext =>
        {
            var httpContext = transformContext.HttpContext;
            var store = httpContext.RequestServices.GetRequiredService<SharedLoginUserStore>();
            var (loginUser, _) = await store.GetLoginUserFromContextAsync(httpContext);

            if (loginUser != null)
            {
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Strangler-User-Id", loginUser.UserId);
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Strangler-User-Name", Uri.EscapeDataString(loginUser.UserName ?? string.Empty));
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Strangler-Org-Code", loginUser.OrganizationCode ?? string.Empty);
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Strangler-Org-Name", Uri.EscapeDataString(loginUser.OrganizationName ?? string.Empty));
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Strangler-Roles", string.Join(",", loginUser.Roles ?? new List<string>()));

                string userJson = JsonSerializer.Serialize(loginUser);
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Strangler-LoginUser", Uri.EscapeDataString(userJson));
            }
        });
    });

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

// ------------------------------------------------------------------------------------------------
// 認証ミドルウェア (未ログインならログイン要求 / 認証済みのみレガシープロキシ / REST API・MCPガード)
// ------------------------------------------------------------------------------------------------
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    // 認証不要なホワイトリストパス
    if (path.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/bff/auth/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/oauth/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Client/", StringComparison.OrdinalIgnoreCase) || // WinForms コマンドディスパッチ
        path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var store = context.RequestServices.GetRequiredService<SharedLoginUserStore>();
    var (loginUser, _) = await store.GetLoginUserFromContextAsync(context);

    // 1. REST API (/api/*) および MCP (/api/bff/mcp/*) の認証ガード
    if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        if (loginUser == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Unauthorized",
                statusCode = 401,
                message = "認証が必要です。POST /oauth/token (grant_type=password) で Bearer トークンを取得するか、ブラウザで /login からログインしてください。"
            }));
            return;
        }

        await next();
        return;
    }

    // 2. ブラウザ画面・レガシーViewアクセス (例: /LegacyPortal.aspx, /, /legacy-api/* 等) の未ログインガード
    if (loginUser == null)
    {
        string returnUrl = context.Request.Path + context.Request.QueryString.Value;
        context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return;
    }

    // 認証済みユーザーは後続コントローラーまたは YARP プロキシ (LegacyMvc) へ進む
    await next();
});

// GET /login: モダン側 Web ログイン画面を返却
app.MapGet("/login", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "login.html"));
});

// コントローラー (OAuthController, RestApiController, ClientController, McpController 等)
app.MapControllers();

// 組み込み YARP リバースプロキシ (認証済みリクエストを LegacyMvc :5001 へ転送)
app.MapReverseProxy();

app.Run();
