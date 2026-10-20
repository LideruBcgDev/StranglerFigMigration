Imports System
Imports System.Linq
Imports System.Runtime.InteropServices
Imports LegacyMvc.MDL.Commands
Imports LegacyMvc.PRM
Imports Microsoft.EntityFrameworkCore

Namespace Data

    Public Class LegacyStranglerDbContext
        Inherits DbContext

        Public Sub New(options As DbContextOptions(Of LegacyStranglerDbContext))
            MyBase.New(options)
        End Sub

        Public Property UsrTbl As DbSet(Of UsrTblEntity)
        Public Property ItmTbl As DbSet(Of ItmTblEntity)

        Protected Overrides Sub OnModelCreating(modelBuilder As ModelBuilder)
            MyBase.OnModelCreating(modelBuilder)

            modelBuilder.Entity(Of UsrTblEntity)(Sub(entity)
                                                     entity.ToTable("USR_TBL")
                                                     entity.HasKey(Function(e) e.UsrId)
                                                 End Sub)

            modelBuilder.Entity(Of ItmTblEntity)(Sub(entity)
                                                     entity.ToTable("ITM_TBL")
                                                     entity.HasKey(Function(e) e.ItmCd)
                                                 End Sub)
        End Sub

        Public Shared Function Create(configuredProvider As String, connStr As String, <Out> ByRef providerStatus As String) As LegacyStranglerDbContext
            Dim builder As New DbContextOptionsBuilder(Of LegacyStranglerDbContext)()
            Dim prov As String = If(configuredProvider, "SqlServer").Trim().ToLowerInvariant()

            Select Case prov
                Case "oracle"
                    builder.UseOracle(connStr)
                Case "postgres", "postgresql"
                    builder.UseNpgsql(connStr)
                Case "sqlserver", "mssql"
                    builder.UseSqlServer(connStr)
                Case Else
                    builder.UseInMemoryDatabase("LegacyStranglerSharedDb")
            End Select

            Try
                Dim db As New LegacyStranglerDbContext(builder.Options)
                EnsureSeeded(db)
                providerStatus = $"Active ({configuredProvider})"
                Return db
            Catch ex As Exception
                Dim fallbackBuilder As New DbContextOptionsBuilder(Of LegacyStranglerDbContext)()
                fallbackBuilder.UseInMemoryDatabase("LegacyStranglerFallbackDb")
                Dim fallbackDb As New LegacyStranglerDbContext(fallbackBuilder.Options)
                EnsureSeeded(fallbackDb)
                providerStatus = $"Configured={configuredProvider} (Fallback to EF Core InMemory DB: {ex.GetType().Name})"
                Return fallbackDb
            End Try
        End Function

        Public Shared Sub EnsureSeeded(db As LegacyStranglerDbContext)
            If Not db.ItmTbl.Any() Then
                db.ItmTbl.AddRange(
                    New ItmTblEntity With {
                        .ItmCd = "ITM-001",
                        .ItmNm = "産業用スマートセンサー Type-A",
                        .Category = "IoTデバイス",
                        .UnitPrice = 48000D,
                        .StockQty = 120,
                        .ApprovalStatus = "PENDING (承認待ち)",
                        .UpdatedAtUtc = DateTime.UtcNow
                    },
                    New ItmTblEntity With {
                        .ItmCd = "ITM-002",
                        .ItmNm = "エッジAIゲートウェイユニット Pro",
                        .Category = "ネットワーク機器",
                        .UnitPrice = 195000D,
                        .StockQty = 35,
                        .ApprovalStatus = "PENDING (承認待ち)",
                        .UpdatedAtUtc = DateTime.UtcNow
                    },
                    New ItmTblEntity With {
                        .ItmCd = "ITM-003",
                        .ItmNm = "クラウド監視ライセンス (年間契約)",
                        .Category = "ソフトウェア",
                        .UnitPrice = 120000D,
                        .StockQty = 999,
                        .ApprovalStatus = "DRAFT (未申請)",
                        .UpdatedAtUtc = DateTime.UtcNow
                    }
                )
                db.SaveChanges()
            End If
        End Sub
    End Class

    ''' <summary>
    ''' 【レガシー側 品目(ITM_TBL)管理コマンド (VB.NET)】検索・登録・承認更新
    ''' </summary>
    Public Class LegacyItemQueryCommand
        Public Function Execute(configuredProvider As String, connectionString As String, Optional keyword As String = Nothing) As LegacyItemQueryResult
            Dim status As String = String.Empty
            Using db As LegacyStranglerDbContext = LegacyStranglerDbContext.Create(configuredProvider, connectionString, status)
                Dim query As IQueryable(Of ItmTblEntity) = db.ItmTbl.AsNoTracking().AsQueryable()
                If Not String.IsNullOrWhiteSpace(keyword) Then
                    Dim kw As String = keyword.Trim().ToLower()
                    query = query.Where(Function(i) i.ItmCd.ToLower().Contains(kw) OrElse i.ItmNm.ToLower().Contains(kw) OrElse i.Category.ToLower().Contains(kw))
                End If

                Return New LegacyItemQueryResult With {
                    .Items = query.OrderBy(Function(i) i.ItmCd).ToList(),
                    .ProviderStatus = status
                }
            End Using
        End Function

        Public Function RegisterOrUpdate(
            configuredProvider As String,
            connectionString As String,
            itmCd As String,
            itmNm As String,
            category As String,
            unitPrice As Decimal,
            stockQty As Integer) As ItmTblEntity

            If String.IsNullOrWhiteSpace(itmCd) Then Throw New ArgumentException("品目コード (ITM_CD) は必須です。")
            If String.IsNullOrWhiteSpace(itmNm) Then Throw New ArgumentException("品目名称 (ITM_NM) は必須です。")

            Dim status As String = String.Empty
            Using db As LegacyStranglerDbContext = LegacyStranglerDbContext.Create(configuredProvider, connectionString, status)
                Dim code As String = itmCd.Trim()
                Dim existing As ItmTblEntity = db.ItmTbl.FirstOrDefault(Function(i) i.ItmCd.ToLower() = code.ToLower())

                If existing Is Nothing Then
                    existing = New ItmTblEntity With {
                        .ItmCd = code,
                        .ItmNm = itmNm.Trim(),
                        .Category = If(String.IsNullOrWhiteSpace(category), "一般品目", category.Trim()),
                        .UnitPrice = unitPrice,
                        .StockQty = stockQty,
                        .ApprovalStatus = "DRAFT (未申請)",
                        .UpdatedAtUtc = DateTime.UtcNow
                    }
                    db.ItmTbl.Add(existing)
                Else
                    existing.ItmNm = itmNm.Trim()
                    existing.Category = If(String.IsNullOrWhiteSpace(category), existing.Category, category.Trim())
                    existing.UnitPrice = unitPrice
                    existing.StockQty = stockQty
                    existing.UpdatedAtUtc = DateTime.UtcNow
                End If

                db.SaveChanges()
                Return existing
            End Using
        End Function

        Public Function ApproveItemInTransaction(
            txContext As SharedTransactionContext,
            configuredProvider As String,
            connectionString As String,
            itmCd As String,
            userId As String,
            userName As String) As ItmTblEntity

            If String.IsNullOrWhiteSpace(itmCd) Then Return Nothing

            Dim status As String = String.Empty
            Using db As LegacyStranglerDbContext = LegacyStranglerDbContext.Create(configuredProvider, connectionString, status)
                Dim code As String = itmCd.Trim()
                Dim item As ItmTblEntity = db.ItmTbl.FirstOrDefault(Function(i) i.ItmCd.ToLower() = code.ToLower())
                If item Is Nothing Then Return Nothing

                Dim prevStatus As String = item.ApprovalStatus
                Dim prevBy As String = item.ApprovedBy
                Dim prevAt As DateTime? = item.ApprovedAtUtc

                txContext.RegisterRollbackAction(Sub()
                                                     Dim st As String = String.Empty
                                                     Using rbDb As LegacyStranglerDbContext = LegacyStranglerDbContext.Create(configuredProvider, connectionString, st)
                                                         Dim target = rbDb.ItmTbl.FirstOrDefault(Function(x) x.ItmCd.ToLower() = code.ToLower())
                                                         If target IsNot Nothing Then
                                                             target.ApprovalStatus = prevStatus
                                                             target.ApprovedBy = prevBy
                                                             target.ApprovedAtUtc = prevAt
                                                             rbDb.SaveChanges()
                                                         End If
                                                     End Using
                                                 End Sub)

                item.ApprovalStatus = "APPROVED (品目承認済)"
                item.ApprovedBy = $"{userName} ({userId})"
                item.ApprovedAtUtc = DateTime.UtcNow
                item.UpdatedAtUtc = DateTime.UtcNow
                db.SaveChanges()

                txContext.ExecutionSteps.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [LegacyMvc.MDL (VB.NET) LegacyItemQueryCommand] 品目 '{itmCd}' を承認更新しました (Tx={txContext.TransactionId})")
                Return item
            End Using
        End Function
    End Class

End Namespace
