using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ModernBff.MDL.Data;

/// <summary>
/// ユーザマスタテーブル (USR_TBL)
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

    [Column("ROLES_CSV")]
    [MaxLength(512)]
    public string RolesCsv { get; set; } = string.Empty;

    [Column("IS_ACTIVE")]
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 品目マスタテーブル (ITM_TBL)
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

    [Column("APPROVAL_STATUS")]
    [MaxLength(64)]
    public string ApprovalStatus { get; set; } = "DRAFT (未申請)";

    [Column("APPROVED_BY")]
    [MaxLength(128)]
    public string? ApprovedBy { get; set; }

    [Column("APPROVED_AT_UTC")]
    public DateTime? ApprovedAtUtc { get; set; }

    [Column("UPDATED_AT_UTC")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 新規機能：案件統合管理テーブル (PRJ_TBL) - モダン側 (ModernBff.MDL) で管理
/// 複数の品目 (ITM_TBL) や図面 (DRW_TBL) を関連付け可能（カンマ区切りまたは主コード指定）
/// </summary>
[Table("PRJ_TBL")]
public class PrjTblEntity
{
    [Key]
    [Column("PRJ_ID")]
    [MaxLength(64)]
    public string PrjId { get; set; } = string.Empty;

    [Required]
    [Column("PRJ_NM")]
    [MaxLength(128)]
    public string PrjNm { get; set; } = string.Empty;

    [Column("CUSTOMER_ID")]
    public int CustomerId { get; set; } = 1;

    /// <summary>
    /// 関連付けられた品目コード (複数ある場合はカンマ区切り 例: "ITM-001,ITM-002")
    /// </summary>
    [Column("TARGET_ITM_CD")]
    [MaxLength(256)]
    public string TargetItmCd { get; set; } = string.Empty;

    /// <summary>
    /// 関連付けられたBOM番号
    /// </summary>
    [Column("LINKED_BOM_NO")]
    [MaxLength(128)]
    public string LinkedBomNo { get; set; } = string.Empty;

    /// <summary>
    /// 関連付けられた図面番号 (複数ある場合はカンマ区切り 例: "DRW-2026-001,DRW-2026-002")
    /// </summary>
    [Column("LINKED_DRAWING_NO")]
    [MaxLength(256)]
    public string LinkedDrawingNo { get; set; } = string.Empty;

    [Column("PRJ_STATUS")]
    [MaxLength(64)]
    public string PrjStatus { get; set; } = "DRAFT (未申請)";

    [Column("APPROVED_BY")]
    [MaxLength(128)]
    public string? ApprovedBy { get; set; }

    [Column("UPDATED_AT_UTC")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 新規機能：ワークフロー管理テーブル (WF_TBL) - モダン側 (ModernBff.MDL) で管理
/// ・申請種別 (TARGET_TYPE): "PROJECT" (案件), "ITEM" (品目), "DRAWING" (図面) から申請可能
/// ・ステータス遷移: "APPLIED (申請済・照査待ち)" -> "VERIFIED (照査済・承認待ち)" -> "APPROVED (承認完了)"
/// ・案件 (PROJECT) を承認した場合は、関連づいている品目 (ITM_TBL)・図面 (DRW_TBL)・BOM も一括承認されます。
/// </summary>
[Table("WF_TBL")]
public class WfTblEntity
{
    [Key]
    [Column("WF_ID")]
    [MaxLength(64)]
    public string WfId { get; set; } = string.Empty;

    /// <summary>
    /// 申請対象種別: "PROJECT" (案件) | "ITEM" (品目) | "DRAWING" (図面)
    /// </summary>
    [Column("TARGET_TYPE")]
    [MaxLength(32)]
    public string TargetType { get; set; } = "PROJECT";

    /// <summary>
    /// 申請対象のキー (案件ID: PRJ-1001 / 品目コード: ITM-001 / 図面番号: DRW-2026-001)
    /// </summary>
    [Column("TARGET_ID")]
    [MaxLength(64)]
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// 後方互換用 (案件ID)
    /// </summary>
    [Column("PRJ_ID")]
    [MaxLength(64)]
    public string PrjId { get; set; } = string.Empty;

    [Column("WF_TITLE")]
    [MaxLength(256)]
    public string WfTitle { get; set; } = string.Empty;

    /// <summary>
    /// ステータス: "APPLIED (申請済・照査待ち)" -> "VERIFIED (照査済・承認待ち)" -> "APPROVED (承認完了)"
    /// </summary>
    [Column("WF_STATUS")]
    [MaxLength(64)]
    public string WfStatus { get; set; } = "APPLIED (申請済・照査待ち)";

    [Column("APPLIED_BY")]
    [MaxLength(128)]
    public string? AppliedBy { get; set; }

    [Column("APPLIED_AT_UTC")]
    public DateTime? AppliedAtUtc { get; set; }

    [Column("VERIFIED_BY")]
    [MaxLength(128)]
    public string? VerifiedBy { get; set; }

    [Column("VERIFIED_AT_UTC")]
    public DateTime? VerifiedAtUtc { get; set; }

    [Column("APPROVED_BY")]
    [MaxLength(128)]
    public string? ApprovedBy { get; set; }

    [Column("APPROVED_ORG")]
    [MaxLength(128)]
    public string? ApprovedOrg { get; set; }

    [Column("APPROVED_AT_UTC")]
    public DateTime? ApprovedAtUtc { get; set; }
}

/// <summary>
/// ModernBff.MDL 用 EF Core DbContext (Oracle / PostgreSQL / SQL Server / InMemory 切替対応)
/// </summary>
public class StranglerDbContext : DbContext
{
    public StranglerDbContext(DbContextOptions<StranglerDbContext> options)
        : base(options)
    {
    }

    public DbSet<UsrTblEntity> UsrTbl => Set<UsrTblEntity>();
    public DbSet<ItmTblEntity> ItmTbl => Set<ItmTblEntity>();
    public DbSet<PrjTblEntity> PrjTbl => Set<PrjTblEntity>();
    public DbSet<WfTblEntity> WfTbl => Set<WfTblEntity>();

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

        modelBuilder.Entity<PrjTblEntity>(entity =>
        {
            entity.ToTable("PRJ_TBL");
            entity.HasKey(e => e.PrjId);
        });

        modelBuilder.Entity<WfTblEntity>(entity =>
        {
            entity.ToTable("WF_TBL");
            entity.HasKey(e => e.WfId);
        });
    }

    public static void ConfigureProvider(DbContextOptionsBuilder options, string provider, string connectionString)
    {
        switch ((provider ?? "SqlServer").Trim().ToLowerInvariant())
        {
            case "oracle":
                options.UseOracle(connectionString);
                break;
            case "postgres":
            case "postgresql":
                options.UseNpgsql(connectionString);
                break;
            case "sqlserver":
            case "mssql":
                options.UseSqlServer(connectionString);
                break;
            case "inmemory":
            default:
                options.UseInMemoryDatabase("StranglerFigSharedDb");
                break;
        }
    }

    public static StranglerDbContext CreateWorkingContext(string configuredProvider, string connectionString, out string providerStatus)
    {
        var builder = new DbContextOptionsBuilder<StranglerDbContext>();
        ConfigureProvider(builder, configuredProvider, connectionString);

        try
        {
            var db = new StranglerDbContext(builder.Options);
            EnsureSeeded(db);
            providerStatus = $"Active ({configuredProvider})";
            return db;
        }
        catch (Exception ex)
        {
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
                    ApprovalStatus = "PENDING (承認待ち)",
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new ItmTblEntity
                {
                    ItmCd = "ITM-002",
                    ItmNm = "エッジAIゲートウェイユニット Pro",
                    Category = "ネットワーク機器",
                    UnitPrice = 195000m,
                    StockQty = 35,
                    ApprovalStatus = "PENDING (承認待ち)",
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new ItmTblEntity
                {
                    ItmCd = "ITM-003",
                    ItmNm = "クラウド監視ライセンス (年間契約)",
                    Category = "ソフトウェア",
                    UnitPrice = 120000m,
                    StockQty = 999,
                    ApprovalStatus = "DRAFT (未申請)",
                    UpdatedAtUtc = DateTime.UtcNow
                }
            );
        }

        if (!db.PrjTbl.Any())
        {
            db.PrjTbl.AddRange(
                new PrjTblEntity
                {
                    PrjId = "PRJ-1001",
                    PrjNm = "次世代スマート工場センサー導入案件",
                    CustomerId = 1,
                    TargetItmCd = "ITM-001",
                    LinkedBomNo = "BOM-2026-001",
                    LinkedDrawingNo = "DRW-2026-001",
                    PrjStatus = "IN_REVIEW (WF申請中)",
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new PrjTblEntity
                {
                    PrjId = "PRJ-1002",
                    PrjNm = "全国拠点エッジAIゲートウェイ刷新案件",
                    CustomerId = 2,
                    TargetItmCd = "ITM-002",
                    LinkedBomNo = "BOM-2026-002",
                    LinkedDrawingNo = "DRW-2026-002",
                    PrjStatus = "IN_REVIEW (WF申請中)",
                    UpdatedAtUtc = DateTime.UtcNow
                }
            );
        }

        if (!db.WfTbl.Any())
        {
            db.WfTbl.AddRange(
                new WfTblEntity
                {
                    WfId = "WF-9001",
                    TargetType = "PROJECT",
                    TargetId = "PRJ-1001",
                    PrjId = "PRJ-1001",
                    WfTitle = "[案件申請] 次世代スマート工場センサー導入案件 (関連: ITM-001 / DRW-2026-001)",
                    WfStatus = "VERIFIED (照査済・承認待ち)",
                    AppliedBy = "鈴木 花子 (suzuki)",
                    AppliedAtUtc = DateTime.UtcNow.AddHours(-2),
                    VerifiedBy = "佐藤 健一 (sato)",
                    VerifiedAtUtc = DateTime.UtcNow.AddHours(-1)
                },
                new WfTblEntity
                {
                    WfId = "WF-9002",
                    TargetType = "PROJECT",
                    TargetId = "PRJ-1002",
                    PrjId = "PRJ-1002",
                    WfTitle = "[案件申請] 全国拠点エッジAIゲートウェイ刷新案件 (関連: ITM-002 / DRW-2026-002)",
                    WfStatus = "APPLIED (申請済・照査待ち)",
                    AppliedBy = "鈴木 花子 (suzuki)",
                    AppliedAtUtc = DateTime.UtcNow.AddMinutes(-30)
                }
            );
        }

        db.SaveChanges();
    }
}
