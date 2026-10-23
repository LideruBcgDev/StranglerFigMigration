using Microsoft.AspNetCore.Mvc;

namespace ModernBff.Controllers;

/// <summary>
/// 【モダン Web ログイン画面コントローラー】
/// Program.cs のインラインルーティング (app.MapGet) をコントローラーへ外出しし、責務を分離します。
/// </summary>
[ApiController]
public class LoginController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public LoginController(IWebHostEnvironment env)
    {
        _env = env;
    }

    /// <summary>
    /// GET /login
    /// モダン Web ログイン画面 (login.html) を返します。
    /// </summary>
    [HttpGet("login")]
    public IActionResult GetLoginPage()
    {
        string filePath = Path.Combine(_env.WebRootPath, "login.html");
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("login.html が見つかりません。");
        }
        return PhysicalFile(filePath, "text/html; charset=utf-8");
    }
}
