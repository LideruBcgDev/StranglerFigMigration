using System.Text.Json;
using ModernBff.Data;
using ModernBff.MDL.Data;
using ModernBff.Middleware;
using ModernBff.Services;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Windows サービス対応
builder.Host.UseWindowsService();

builder.Services.AddControllers();

// Swagger / OpenAPI 設定 (BFF REST API, MCP サーバー, OAuth2, Client コントローラー対応)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Strangler Fig ModernBff & BFF REST API",
        Version = "v1",
        Description = "モダンBFF (BFF REST API, MCP Server, OAuth 2.0, Client Dispatcher) の API 仕様書"
    });

    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "OAuth 2.0 Bearer トークン認証。POST /oauth/token で取得した access_token を入力してください。"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// 0. EF Core DbContext の登録 (SqlServer / InMemory 等)
builder.Services.AddDbContext<StranglerDbContext>(options =>
{
    DatabaseConfigurationExtensions.ConfigureStranglerDatabase(
        options,
        builder.Configuration,
        out _,
        out _);
});

// 1. キャッシュ & セッションのセットアップ (インメモリ)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".StranglerFig.Session";
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 2. 認証ストア & モダン側 MCP 実行エンジン登録
builder.Services.AddSingleton<SharedLoginUserStore>();
builder.Services.AddSingleton<ModernBff.Mcp.StranglerMcpEngine>();

// 3. 内蔵 YARP リバースプロキシ (Embedded YARP)
// 自前 API (MCP / Auth / Client等) 以外のリクエスト (レガシー画面 / レガシー REST API 等) を LegacyMvc (:5001) へ転送
// その際、認証済みユーザー情報を HTTP ヘッダーとして注入
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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ModernBff API v1");
    c.RoutePrefix = "swagger";
});

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

// 4. モダン認証ガードミドルウェア (別ファイル外出し: ModernAuthGuardMiddleware)
// ・未ログインの画面アクセス -> /login へリダイレクト
// ・MCP サーバー (/api/bff/mcp/*) および レガシー REST API (/api/*) -> 未認証時は 401
app.UseModernAuthGuard();

// 5. コントローラー (OAuthController, AuthController, McpController, LoginController 等)
app.MapControllers();

// 6. 組み込み YARP リバースプロキシ (認証済みリクエストを LegacyMvc :5001 へ透過転送)
app.MapReverseProxy();

app.Run();
