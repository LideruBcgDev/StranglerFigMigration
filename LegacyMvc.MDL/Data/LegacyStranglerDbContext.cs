using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace LegacyMvc.MDL.Data
{
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

        public static LegacyStranglerDbContext Create(string configuredProvider, string connStr, out string providerStatus)
        {
            var builder = new DbContextOptionsBuilder<LegacyStranglerDbContext>();

            switch ((configuredProvider ?? "SqlServer").Trim().ToLowerInvariant())
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

    /// <summary>
    /// 【レガシー側コマンド3】品目マスタ (ITM_TBL) 検索コマンド (LegacyMvc.MDL)
    /// </summary>
    public class LegacyItemQueryCommand
    {
        public (List<ItmTblEntity> Items, string ProviderStatus) Execute(string configuredProvider, string connectionString, string itmCd = null)
        {
            using (var db = LegacyStranglerDbContext.Create(configuredProvider, connectionString, out string providerStatus))
            {
                var query = db.ItmTbl.AsNoTracking().AsQueryable();
                if (!string.IsNullOrWhiteSpace(itmCd))
                {
                    string code = itmCd.Trim();
                    query = query.Where(i => i.ItmCd.ToLower() == code.ToLower());
                }

                return (query.OrderBy(i => i.ItmCd).ToList(), providerStatus);
            }
        }
    }
}
