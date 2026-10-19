using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ModernBff.Data;

/// <summary>
/// ユーザマスタテーブル (USR_TBL)
/// Oracle / PostgreSQL / SQL Server 共通でマッピング可能な大文字テーブル・カラム定義
/// </summary>
[Table("USR_TBL")]
public class UsrTblEntity
{
    [Key]
    [Column("USR_ID")]
    [MaxLength(64)]
    public string UsrId { get; set; } = string.Empty;

    [Required]
    [Column("PASSWORD")]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Column("USR_NM")]
    [MaxLength(128)]
    public string UsrNm { get; set; } = string.Empty;

    [Column("ORG_CD")]
    [MaxLength(64)]
    public string OrgCd { get; set; } = string.Empty;

    [Column("ORG_NM")]
    [MaxLength(128)]
    public string OrgNm { get; set; } = string.Empty;

    /// <summary>
    /// カンマ区切りの権限ロール (例: "Admin,SalesManager,CustomerRead,CustomerWrite,LegacyAccess")
    /// </summary>
    [Column("ROLES_CSV")]
    [MaxLength(512)]
    public string RolesCsv { get; set; } = string.Empty;

    /// <summary>
    /// 有効フラグ (true=ログイン可能, false=アカウント無効)
    /// </summary>
    [Column("IS_ACTIVE")]
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 品目マスタテーブル (ITM_TBL)
/// モダン側 (ModernBff) とレガシー側 (LegacyMvc) の双方から参照
/// </summary>
[Table("ITM_TBL")]
public class ItmTblEntity
{
    [Key]
    [Column("ITM_CD")]
    [MaxLength(64)]
    public string ItmCd { get; set; } = string.Empty;

    [Required]
    [Column("ITM_NM")]
    [MaxLength(128)]
    public string ItmNm { get; set; } = string.Empty;

    [Column("CATEGORY")]
    [MaxLength(64)]
    public string Category { get; set; } = string.Empty;

    [Column("UNIT_PRICE")]
    public decimal UnitPrice { get; set; }

    [Column("STOCK_QTY")]
    public int StockQty { get; set; }

    [Column("UPDATED_AT_UTC")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// EF Core DbContext (Oracle / PostgreSQL / SQL Server / InMemory 切り替え対応)
/// </summary>
public class StranglerDbContext : DbContext
{
    public StranglerDbContext(DbContextOptions<StranglerDbContext> options)
        : base(options)
    {
    }

    public DbSet<UsrTblEntity> UsrTbl => Set<UsrTblEntity>();
    public DbSet<ItmTblEntity> ItmTbl => Set<ItmTblEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UsrTblEntity>(entity =>
        {
            entity.ToTable("USR_TBL");
            entity.HasKey(e => e.UsrId);
        });

        modelBuilder.Entity<ItmTblEntity>(entity =>
        {
            entity.ToTable("ITM_TBL");
            entity.HasKey(e => e.ItmCd);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
        });
    }
}

/// <summary>
/// appsettings.json の Database:Provider ("Oracle" | "Postgres" | "SqlServer" | "InMemory") に応じて
/// DbContextOptions を構成するファクトリ＆初期データシーダー
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

        switch (activeProvider.Trim().ToLowerInvariant())
        {
            case "oracle":
                options.UseOracle(activeConnectionString);
                break;

            case "postgres":
            case "postgresql":
                options.UseNpgsql(activeConnectionString);
                break;

            case "sqlserver":
            case "mssql":
                options.UseSqlServer(activeConnectionString);
                break;

            case "inmemory":
            default:
                options.UseInMemoryDatabase("StranglerFigSharedDb");
                break;
        }
    }

    /// <summary>
    /// 選択された DB プロバイダーで USR_TBL / ITM_TBL を検索し、
    /// ローカル検証環境で実 DB サーバー（Oracle / Postgres / SQL Server）が未起動の場合は
    /// 自動的に InMemory DB の初期シードデータへフォールバックして検証を継続できるようにします。
    /// </summary>
    public static StranglerDbContext CreateWorkingContextWithFallback(
        IServiceProvider sp,
        IConfiguration configuration,
        ILogger logger,
        out string providerStatus)
    {
        string configuredProvider = configuration["Database:Provider"] ?? "SqlServer";
        try
        {
            var db = sp.GetRequiredService<StranglerDbContext>();
            EnsureSeeded(db);
            providerStatus = $"Active ({configuredProvider})";
            return db;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Configured DB provider '{Provider}' is unreachable. Falling back to EF Core InMemory DB with seeded USR_TBL / ITM_TBL.", configuredProvider);
            var fallbackOptions = new DbContextOptionsBuilder<StranglerDbContext>()
                .UseInMemoryDatabase("StranglerFigFallbackDb")
                .Options;
            var fallbackDb = new StranglerDbContext(fallbackOptions);
            EnsureSeeded(fallbackDb);
            providerStatus = $"Configured={configuredProvider} (Fallback to EF Core InMemory DB: {ex.GetType().Name})";
            return fallbackDb;
        }
    }

    public static void EnsureSeeded(StranglerDbContext db)
    {
        if (db.Database.IsInMemory())
        {
            db.Database.EnsureCreated();
        }

        if (!db.UsrTbl.Any())
        {
            db.UsrTbl.AddRange(
                new UsrTblEntity
                {
                    UsrId = "yamada",
                    Password = "pass123",
                    UsrNm = "山田 太郎",
                    OrgCd = "ORG-SALES-01",
                    OrgNm = "第一営業本部 エンタープライズ営業部",
                    RolesCsv = "Admin,SalesManager,CustomerRead,CustomerWrite,LegacyAccess",
                    IsActive = true
                },
                new UsrTblEntity
                {
                    UsrId = "suzuki",
                    Password = "pass123",
                    UsrNm = "鈴木 花子",
                    OrgCd = "ORG-CS-02",
                    OrgNm = "カスタマーサクセス推進室",
                    RolesCsv = "Operator,CustomerRead",
                    IsActive = true
                },
                new UsrTblEntity
                {
                    UsrId = "sato",
                    Password = "pass123",
                    UsrNm = "佐藤 健一",
                    OrgCd = "ORG-AUDIT-99",
                    OrgNm = "内部監査室",
                    RolesCsv = "Auditor,CustomerRead,LegacyAccess",
                    IsActive = true
                },
                new UsrTblEntity
                {
                    UsrId = "disabled_user",
                    Password = "pass123",
                    UsrNm = "退職 太郎 (無効ユーザ)",
                    OrgCd = "ORG-NONE",
                    OrgNm = "未所属",
                    RolesCsv = "None",
                    IsActive = false
                }
            );
        }

        if (!db.ItmTbl.Any())
        {
            db.ItmTbl.AddRange(
                new ItmTblEntity
                {
                    ItmCd = "ITM-001",
                    ItmNm = "産業用スマートセンサー Type-A",
                    Category = "IoTデバイス",
                    UnitPrice = 48000m,
                    StockQty = 120,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new ItmTblEntity
                {
                    ItmCd = "ITM-002",
                    ItmNm = "エッジAIゲートウェイユニット Pro",
                    Category = "ネットワーク機器",
                    UnitPrice = 195000m,
                    StockQty = 35,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new ItmTblEntity
                {
                    ItmCd = "ITM-003",
                    ItmNm = "クラウド監視ライセンス (年間契約)",
                    Category = "ソフトウェア",
                    UnitPrice = 120000m,
                    StockQty = 999,
                    UpdatedAtUtc = DateTime.UtcNow
                }
            );
        }

        db.SaveChanges();
    }
}
