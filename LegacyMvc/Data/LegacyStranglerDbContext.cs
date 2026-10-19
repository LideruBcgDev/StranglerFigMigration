using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Configuration;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace LegacyMvc.Data
{
    /// <summary>
    /// ユーザマスタテーブル (USR_TBL) - LegacyMvc 側 EF Core エンティティ
    /// </summary>
    [Table("USR_TBL")]
    public class UsrTblEntity
    {
        [Key]
        [Column("USR_ID")]
        [MaxLength(64)]
        public string UsrId { get; set; }

        [Required]
        [Column("PASSWORD")]
        [MaxLength(128)]
        public string Password { get; set; }

        [Required]
        [Column("USR_NM")]
        [MaxLength(128)]
        public string UsrNm { get; set; }

        [Column("ORG_CD")]
        [MaxLength(64)]
        public string OrgCd { get; set; }

        [Column("ORG_NM")]
        [MaxLength(128)]
        public string OrgNm { get; set; }

        [Column("ROLES_CSV")]
        [MaxLength(512)]
        public string RolesCsv { get; set; }

        [Column("IS_ACTIVE")]
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// 品目マスタテーブル (ITM_TBL) - LegacyMvc 側 EF Core エンティティ
    /// </summary>
    [Table("ITM_TBL")]
    public class ItmTblEntity
    {
        [Key]
        [Column("ITM_CD")]
        [MaxLength(64)]
        public string ItmCd { get; set; }

        [Required]
        [Column("ITM_NM")]
        [MaxLength(128)]
        public string ItmNm { get; set; }

        [Column("CATEGORY")]
        [MaxLength(64)]
        public string Category { get; set; }

        [Column("UNIT_PRICE")]
        public decimal UnitPrice { get; set; }

        [Column("STOCK_QTY")]
        public int StockQty { get; set; }

        [Column("UPDATED_AT_UTC")]
        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>
    /// LegacyMvc (.NET Framework 4.8.1) 用 EF Core DbContext。
    /// Web.config の appSettings["DatabaseProvider"] ("Oracle" | "Postgres" | "SqlServer" | "InMemory") と
    /// connectionStrings セクションの設定によって接続先 DB を切り替えます。
    /// </summary>
    public class LegacyStranglerDbContext : DbContext
    {
        public LegacyStranglerDbContext(DbContextOptions<LegacyStranglerDbContext> options)
            : base(options)
        {
        }

        public DbSet<UsrTblEntity> UsrTbl { get; set; }
        public DbSet<ItmTblEntity> ItmTbl { get; set; }

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
            });
        }

        /// <summary>
        /// Web.config の設定から DbContext を構築し、実 DB サーバー未起動時は EF Core InMemory へフォールバックします。
        /// </summary>
        public static LegacyStranglerDbContext CreateFromWebConfig(out string providerStatus)
        {
            string configuredProvider = ConfigurationManager.AppSettings["DatabaseProvider"] ?? "SqlServer";
            string connStr = ConfigurationManager.ConnectionStrings[configuredProvider]?.ConnectionString ?? string.Empty;

            var builder = new DbContextOptionsBuilder<LegacyStranglerDbContext>();

            switch (configuredProvider.Trim().ToLowerInvariant())
            {
                case "oracle":
                    builder.UseOracle(connStr);
                    break;
                case "postgres":
                case "postgresql":
                    builder.UseNpgsql(connStr);
                    break;
                case "sqlserver":
                case "mssql":
                    builder.UseSqlServer(connStr);
                    break;
                case "inmemory":
                default:
                    builder.UseInMemoryDatabase("LegacyStranglerSharedDb");
                    break;
            }

            try
            {
                var db = new LegacyStranglerDbContext(builder.Options);
                EnsureSeeded(db);
                providerStatus = $"Active ({configuredProvider})";
                return db;
            }
            catch (Exception ex)
            {
                var fallbackBuilder = new DbContextOptionsBuilder<LegacyStranglerDbContext>()
                    .UseInMemoryDatabase("LegacyStranglerFallbackDb");
                var fallbackDb = new LegacyStranglerDbContext(fallbackBuilder.Options);
                EnsureSeeded(fallbackDb);
                providerStatus = $"Configured={configuredProvider} (Fallback to EF Core InMemory DB: {ex.GetType().Name})";
                return fallbackDb;
            }
        }

        public static void EnsureSeeded(LegacyStranglerDbContext db)
        {
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
                db.SaveChanges();
            }
        }
    }
}
