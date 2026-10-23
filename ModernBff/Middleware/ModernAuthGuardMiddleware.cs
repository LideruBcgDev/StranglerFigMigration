using System.Text.Json;
using ModernBff.Services;

namespace ModernBff.Middleware;

/// <summary>
/// 【モダン側 認証ガードミドルウェア】
/// 1. MCP サーバー (/api/bff/mcp/*) および レガシー REST API (/api/*):
///    未認証時は 401 Unauthorized を返却 (OAuth Bearer トークンまたは Cookie 必須)
/// 2. Web 画面・レガシーView (例: /LegacyPortal.aspx, /View/* 等):
///    未ログイン時はモダン側ログイン画面 (/login?returnUrl=...) へ 302 リダイレクト
/// 3. 認証不要パス (/login, /api/bff/auth/*, /oauth/*, /Client/*, 静的ファイル等):
///    そのまま後続へスルー
/// </summary>
public class ModernAuthGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ModernAuthGuardMiddleware> _logger;

    public ModernAuthGuardMiddleware(RequestDelegate next, ILogger<ModernAuthGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, SharedLoginUserStore loginUserStore)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 認証不要なホワイトリストパス
        if (IsWhitelistedPath(path))
        {
            await _next(context);
            return;
        }

        var (loginUser, _) = await loginUserStore.GetLoginUserFromContextAsync(context);

        // 1. モダン側 MCP サーバー (/api/bff/mcp/*) および レガシー REST API (/api/*) の認証ガード
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
                    message = "認証が必要です。POST /oauth/token (grant_type=password) で Bearer トークンを取得するか、/login でログインしてください。"
                }));
                return;
            }

            await _next(context);
            return;
        }

        // 2. ブラウザ画面・レガシーView (例: /LegacyPortal.aspx, /View/* 等) の未ログインガード
        if (loginUser == null)
        {
            string returnUrl = context.Request.Path + context.Request.QueryString.Value;
            context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
            return;
        }

        // 認証済みユーザーは後続 (コントローラーまたは YARP プロキシ) へ進む
        await _next(context);
    }

    private static bool IsWhitelistedPath(string path)
    {
        return path.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) || // Swagger UI & OpenAPI spec
               path.StartsWith("/api/bff/auth/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/oauth/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/Client/", StringComparison.OrdinalIgnoreCase) || // WinForms コマンドディスパッチ
               path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }
}

public static class ModernAuthGuardMiddlewareExtensions
{
    public static IApplicationBuilder UseModernAuthGuard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ModernAuthGuardMiddleware>();
    }
}
