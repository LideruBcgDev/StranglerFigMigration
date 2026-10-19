Imports System
Imports System.Collections.Generic
Imports System.ComponentModel.DataAnnotations
Imports System.ComponentModel.DataAnnotations.Schema
Imports System.Linq
Imports System.Runtime.InteropServices
Imports Microsoft.EntityFrameworkCore

Namespace Data

    <Table("USR_TBL")>
    Public Class UsrTblEntity
        <Key>
        <Column("USR_ID")>
        <MaxLength(64)>
        Public Property UsrId As String = String.Empty

        <Required>
        <Column("PASSWORD")>
        <MaxLength(128)>
        Public Property Password As String = String.Empty

        <Required>
        <Column("USR_NM")>
        <MaxLength(128)>
        Public Property UsrNm As String = String.Empty

        <Column("ORG_CD")>
        <MaxLength(64)>
        Public Property OrgCd As String = String.Empty

        <Column("ORG_NM")>
        <MaxLength(128)>
        Public Property OrgNm As String = String.Empty

        <Column("ROLES_CSV")>
        <MaxLength(512)>
        Public Property RolesCsv As String = String.Empty

        <Column("IS_ACTIVE")>
        Public Property IsActive As Boolean = True
    End Class

    <Table("ITM_TBL")>
    Public Class ItmTblEntity
        <Key>
        <Column("ITM_CD")>
        <MaxLength(64)>
        Public Property ItmCd As String = String.Empty

        <Required>
        <Column("ITM_NM")>
        <MaxLength(128)>
        Public Property ItmNm As String = String.Empty

        <Column("CATEGORY")>
        <MaxLength(64)>
        Public Property Category As String = String.Empty

        <Column("UNIT_PRICE")>
        Public Property UnitPrice As Decimal

        <Column("STOCK_QTY")>
        Public Property StockQty As Integer

        <Column("UPDATED_AT_UTC")>
        Public Property UpdatedAtUtc As DateTime = DateTime.UtcNow
    End Class

    Public Class LegacyItemQueryResult
        Public Property Items As List(Of ItmTblEntity) = New List(Of ItmTblEntity)()
        Public Property ProviderStatus As String = String.Empty
    End Class

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
                        .UpdatedAtUtc = DateTime.UtcNow
                    },
                    New ItmTblEntity With {
                        .ItmCd = "ITM-002",
                        .ItmNm = "エッジAIゲートウェイユニット Pro",
                        .Category = "ネットワーク機器",
                        .UnitPrice = 195000D,
                        .StockQty = 35,
                        .UpdatedAtUtc = DateTime.UtcNow
                    },
                    New ItmTblEntity With {
                        .ItmCd = "ITM-003",
                        .ItmNm = "クラウド監視ライセンス (年間契約)",
                        .Category = "ソフトウェア",
                        .UnitPrice = 120000D,
                        .StockQty = 999,
                        .UpdatedAtUtc = DateTime.UtcNow
                    }
                )
                db.SaveChanges()
            End If
        End Sub
    End Class

    ''' <summary>
    ''' 【レガシー側コマンド3 (VB.NET)】品目マスタ (ITM_TBL) 検索コマンド (LegacyMvc.MDL.Data.LegacyItemQueryCommand)
    ''' </summary>
    Public Class LegacyItemQueryCommand
        Public Function Execute(configuredProvider As String, connectionString As String, Optional itmCd As String = Nothing) As LegacyItemQueryResult
            Dim status As String = String.Empty
            Using db As LegacyStranglerDbContext = LegacyStranglerDbContext.Create(configuredProvider, connectionString, status)
                Dim query As IQueryable(Of ItmTblEntity) = db.ItmTbl.AsNoTracking().AsQueryable()
                If Not String.IsNullOrWhiteSpace(itmCd) Then
                    Dim code As String = itmCd.Trim().ToLower()
                    query = query.Where(Function(i) i.ItmCd.ToLower() = code)
                End If

                Return New LegacyItemQueryResult With {
                    .Items = query.OrderBy(Function(i) i.ItmCd).ToList(),
                    .ProviderStatus = status
                }
            End Using
        End Function
    End Class

End Namespace
