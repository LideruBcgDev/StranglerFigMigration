Imports System
Imports System.Linq
Imports System.Runtime.CompilerServices
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

            Try
                ApplyProvider(builder, prov, connStr)
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

        Private Shared Sub ApplyProvider(builder As DbContextOptionsBuilder(Of LegacyStranglerDbContext), prov As String, connStr As String)
            Select Case prov
                Case "oracle"
                    ConfigureOracle(builder, connStr)
                Case "postgres", "postgresql"
                    ConfigurePostgres(builder, connStr)
                Case "sqlserver", "mssql"
                    ConfigureSqlServer(builder, connStr)
                Case Else
                    builder.UseInMemoryDatabase("LegacyStranglerSharedDb")
            End Select
        End Sub

        <MethodImpl(MethodImplOptions.NoInlining)>
        Private Shared Sub ConfigureOracle(builder As DbContextOptionsBuilder(Of LegacyStranglerDbContext), connStr As String)
            builder.UseOracle(connStr)
        End Sub

        <MethodImpl(MethodImplOptions.NoInlining)>
        Private Shared Sub ConfigurePostgres(builder As DbContextOptionsBuilder(Of LegacyStranglerDbContext), connStr As String)
            builder.UseNpgsql(connStr)
        End Sub

        <MethodImpl(MethodImplOptions.NoInlining)>
        Private Shared Sub ConfigureSqlServer(builder As DbContextOptionsBuilder(Of LegacyStranglerDbContext), connStr As String)
            builder.UseSqlServer(connStr)
        End Sub

        Public Shared Sub EnsureSeeded(db As LegacyStranglerDbContext)
            If Not db.ItmTbl.Any() Then
                db.ItmTbl.AddRange(
                    New ItmTblEntity With {
                        .ItmCd = "ITM-001",
                        .Ver = 1,
                        .Rev = "A",
                        .ItmNm = "産業用スマートセンサー Type-A",
                        .Category = "IoTデバイス",
                        .UnitPrice = 48000D,
                        .StockQty = 120,
                        .ApprovalStatus = "PENDING (承認待ち)",
                        .UpdatedAtUtc = DateTime.UtcNow
                    },
                    New ItmTblEntity With {
                        .ItmCd = "ITM-002",
                        .Ver = 2,
                        .Rev = "B",
                        .ItmNm = "エッジAIゲートウェイユニット Pro",
                        .Category = "ネットワーク機器",
                        .UnitPrice = 195000D,
                        .StockQty = 35,
                        .ApprovalStatus = "PENDING (承認待ち)",
                        .UpdatedAtUtc = DateTime.UtcNow
                    },
                    New ItmTblEntity With {
                        .ItmCd = "ITM-003",
                        .Ver = 1,
                        .Rev = "A",
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
    ''' 【レガシー側 品目(ITM_TBL)管理コマンド (VB.NET)】検索・登録・複製・改訂・承認更新
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
            stockQty As Integer,
            Optional ver As Integer = 1,
            Optional rev As String = "A") As ItmTblEntity

            If String.IsNullOrWhiteSpace(itmCd) Then Throw New ArgumentException("品目コード (ITM_CD) は必須です。")
            If String.IsNullOrWhiteSpace(itmNm) Then Throw New ArgumentException("品目名称 (ITM_NM) は必須です。")

            Dim status As String = String.Empty
            Using db As LegacyStranglerDbContext = LegacyStranglerDbContext.Create(configuredProvider, connectionString, status)
                Dim code As String = itmCd.Trim()
                Dim existing As ItmTblEntity = db.ItmTbl.FirstOrDefault(Function(i) i.ItmCd.ToLower() = code.ToLower())

                If existing Is Nothing Then
                    existing = New ItmTblEntity With {
                        .ItmCd = code,
                        .Ver = If(ver < 1, 1, ver),
                        .Rev = If(String.IsNullOrWhiteSpace(rev), "A", rev.Trim()),
                        .ItmNm = itmNm.Trim(),
                        .Category = If(String.IsNullOrWhiteSpace(category), "一般品目", category.Trim()),
                        .UnitPrice = unitPrice,
                        .StockQty = stockQty,
                        .ApprovalStatus = "DRAFT (未申請)",
                        .UpdatedAtUtc = DateTime.UtcNow
                    }
                    db.ItmTbl.Add(existing)
                Else
                    existing.Ver = If(ver < 1, existing.Ver, ver)
                    existing.Rev = If(String.IsNullOrWhiteSpace(rev), existing.Rev, rev.Trim())
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

        ''' <summary>
        ''' 選択した品目 (sourceItmCd) を複製し、ItmTblEntity.CreateDuplicateRecord で生成した新レコードを DB に保存して返します。
        ''' </summary>
        Public Function DuplicateItem(
            configuredProvider As String,
            connectionString As String,
            sourceItmCd As String,
            newItmCd As String,
            Optional newItmNm As String = Nothing,
            Optional ByRef outSourceSnapshot As ItmTblEntity = Nothing) As ItmTblEntity

            If String.IsNullOrWhiteSpace(sourceItmCd) Then Throw New ArgumentException("複製元の品目コード (SourceItmCd) は必須です。")

            Dim status As String = String.Empty
            Using db As LegacyStranglerDbContext = LegacyStranglerDbContext.Create(configuredProvider, connectionString, status)
                Dim srcCode As String = sourceItmCd.Trim()
                Dim source As ItmTblEntity = db.ItmTbl.AsNoTracking().FirstOrDefault(Function(i) i.ItmCd.ToLower() = srcCode.ToLower())
                If source Is Nothing Then
                    Throw New KeyNotFoundException($"複製元の品目 '{srcCode}' が ITM_TBL に存在しません。")
                End If
                outSourceSnapshot = source

                ' 新コードが未指定または既存重複の場合は連番を付与
                Dim candidateCd As String = If(String.IsNullOrWhiteSpace(newItmCd), $"{source.ItmCd}-CPY", newItmCd.Trim())
                Dim suffix As Integer = 2
                While db.ItmTbl.Any(Function(i) i.ItmCd.ToLower() = candidateCd.ToLower())
                    candidateCd = $"{source.ItmCd}-CPY{suffix}"
                    suffix += 1
                End While

                ' エンティティのドメインメソッド CreateDuplicateRecord で複製レコードを生成
                Dim duplicated As ItmTblEntity = source.CreateDuplicateRecord(candidateCd, newItmNm)
                db.ItmTbl.Add(duplicated)
                db.SaveChanges()
                Return duplicated
            End Using
        End Function

        ''' <summary>
        ''' 選択した品目 (targetItmCd) を改訂し、ItmTblEntity.CreateRevisedRecord で生成した改訂内容を DB に反映して返します。
        ''' </summary>
        Public Function ReviseItem(
            configuredProvider As String,
            connectionString As String,
            targetItmCd As String,
            Optional customNextRev As String = Nothing,
            Optional ByRef outBeforeSnapshot As ItmTblEntity = Nothing) As ItmTblEntity

            If String.IsNullOrWhiteSpace(targetItmCd) Then Throw New ArgumentException("改訂対象の品目コード (TargetItmCd) は必須です。")

            Dim status As String = String.Empty
            Using db As LegacyStranglerDbContext = LegacyStranglerDbContext.Create(configuredProvider, connectionString, status)
                Dim code As String = targetItmCd.Trim()
                Dim existing As ItmTblEntity = db.ItmTbl.FirstOrDefault(Function(i) i.ItmCd.ToLower() = code.ToLower())
                If existing Is Nothing Then
                    Throw New KeyNotFoundException($"改訂対象の品目 '{code}' が ITM_TBL に存在しません。")
                End If

                ' 改訂前スナップショットを保持
                outBeforeSnapshot = ItmTblEntity.CreateWithAllFields(
                    existing.ItmCd,
                    existing.Ver,
                    existing.Rev,
                    existing.ItmNm,
                    existing.Category,
                    existing.UnitPrice,
                    existing.StockQty,
                    existing.ApprovalStatus,
                    existing.ApprovedBy,
                    existing.ApprovedAtUtc,
                    existing.UpdatedAtUtc)

                ' エンティティのドメインメソッド CreateRevisedRecord で改訂後レコードを生成
                Dim revised As ItmTblEntity = existing.CreateRevisedRecord(customNextRev)

                existing.Ver = revised.Ver
                existing.Rev = revised.Rev
                existing.ItmNm = revised.ItmNm
                existing.Category = revised.Category
                existing.UnitPrice = revised.UnitPrice
                existing.StockQty = revised.StockQty
                existing.ApprovalStatus = revised.ApprovalStatus
                existing.ApprovedBy = revised.ApprovedBy
                existing.ApprovedAtUtc = revised.ApprovedAtUtc
                existing.UpdatedAtUtc = revised.UpdatedAtUtc

                db.SaveChanges()
                Return revised
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
