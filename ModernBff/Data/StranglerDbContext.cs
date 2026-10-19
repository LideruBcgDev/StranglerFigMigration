using Microsoft.EntityFrameworkCore;
using ModernBff.MDL.Data;

namespace ModernBff.Data;

/// <summary>
/// ModernBff (Web API 層) から ModernBff.MDL.Data.StranglerDbContext を構成するためのブリッジ拡張クラス。
/// 実際のエンティティ・DbContext・コマンド処理は ModernBff.MDL プロジェクトに集約されています。
/// </summary>
public static class DatabaseConfigurationExtensions
{
    public static void ConfigureStranglerDatabase(
        DbContextOptionsBuilder options,
        IConfiguration configuration,
        out string activeProvider,
        out string activeConnectionString)
    {
        activeProvider = configuration["Database:Provider"] ?? "SqlServer";
        activeConnectionString = configuration.GetConnectionString(activeProvider)
            ?? configuration["Database:ConnectionString"]
            ?? string.Empty;

        StranglerDbContext.ConfigureProvider(options, activeProvider, activeConnectionString);
    }

    public static StranglerDbContext CreateWorkingContextWithFallback(
        IServiceProvider sp,
        IConfiguration configuration,
        ILogger logger,
        out string providerStatus)
    {
        string configuredProvider = configuration["Database:Provider"] ?? "SqlServer";
        string connStr = configuration.GetConnectionString(configuredProvider)
            ?? configuration["Database:ConnectionString"]
            ?? string.Empty;

        return StranglerDbContext.CreateWorkingContext(configuredProvider, connStr, out providerStatus);
    }
}
