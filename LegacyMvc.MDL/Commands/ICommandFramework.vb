Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports LegacyMvc.MDL.Data

Namespace Commands

    ''' <summary>
    ''' 【レガシー側共通コマンドインターフェース (LegacyMvc.MDL - VB.NET)】
    ''' LegacyMvc のすべての実処理を受け持つコマンドの基本インターフェースです。
    ''' </summary>
    Public Interface ICommand
        ReadOnly Property IParam() As IParamBase
        ReadOnly Property OParam() As OParamBase
        Property ResponseType() As String
        Sub Execute()
        Sub Validate()
    End Interface

    ''' <summary>
    ''' IParamBase に格納されるログインユーザ情報 (LoginUser)。
    ''' ・ViewController では HttpContext.User.Identity (IIdentity) から取得して設定
    ''' ・ClientController では Windows Forms 側で設定された command.IParam.LoginUser から取得
    ''' </summary>
    Public Class CommandLoginUser
        Public Property UserId As String = String.Empty
        Public Property UserName As String = String.Empty
        Public Property OrganizationCode As String = String.Empty
        Public Property OrganizationName As String = String.Empty
        Public Property Roles As New List(Of String)()
        Public Property AuthenticationMethod As String = String.Empty
        Public Property IsAuthenticated As Boolean = False
        ''' <summary>
        ''' どのように LoginUser が設定されたかの証跡
        ''' 例: "ViewController (IIdentity)" / "ClientController (WinForms IParam.LoginUser)" / "ModernBff (Redis SharedSession)"
        ''' </summary>
        Public Property SourceChannel As String = String.Empty
    End Class

    ''' <summary>
    ''' 【入力パラメータ基底クラス (IParamBase)】
    ''' Session 情報および LoginUser 情報を保持し、Validate() でコマンド利用可否の判定に使用されます。
    ''' </summary>
    Public Class IParamBase
        ''' <summary>
        ''' ASP.NET Session ID (または分散セッションキー)
        ''' </summary>
        Public Property SessionId As String = String.Empty

        ''' <summary>
        ''' Session 情報ディクショナリ (レガシー側で Session 変数をコマンドへ引き渡すための領域)
        ''' </summary>
        Public Property SessionData As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' コマンド実行ユーザ情報 (ViewController は IIdentity から設定、ClientController は WinForms 送信値から取得)
        ''' </summary>
        Public Property LoginUser As New CommandLoginUser()

        ''' <summary>
        ''' 実行対象コマンドID (権限チェック用)
        ''' </summary>
        Public Property CommandId As String = String.Empty
    End Class

    ''' <summary>
    ''' 【出力パラメータ基底クラス (OParamBase)】
    ''' コマンドの認可検証結果 (Validate) および実行結果 (Execute) を保持します。
    ''' </summary>
    Public Class OParamBase
        Public Property IsSuccess As Boolean = False
        Public Property ValidationPassed As Boolean = False
        Public Property ExecutedCommandName As String = String.Empty
        Public Property AuthorizedUserId As String = String.Empty
        Public Property AuthorizedUserName As String = String.Empty
        Public Property LoginUserSource As String = String.Empty
        Public Property SessionIdUsed As String = String.Empty
        Public Property PermissionCheckResult As String = String.Empty
        Public Property ErrorMessage As String = String.Empty
        Public Property ExecutedAtUtc As DateTime = DateTime.UtcNow
    End Class

    ''' <summary>
    ''' コマンド利用可否 (認可) と共通実行フローを提供する抽象基底クラス
    ''' </summary>
    Public MustInherit Class CommandBase(Of TIParam As {IParamBase, New}, TOParam As {OParamBase, New})
        Implements ICommand

        Protected ReadOnly _iParam As TIParam
        Protected ReadOnly _oParam As TOParam
        Private _responseType As String = "application/json"

        Protected Sub New(Optional iParam As TIParam = Nothing)
            _iParam = If(iParam, New TIParam())
            _oParam = New TOParam() With {
                .ExecutedCommandName = Me.GetType().Name
            }
        End Sub

        Public ReadOnly Property TypedIParam As TIParam
            Get
                Return _iParam
            End Get
        End Property

        Public ReadOnly Property TypedOParam As TOParam
            Get
                Return _oParam
            End Get
        End Property

        Public ReadOnly Property IParam As IParamBase Implements ICommand.IParam
            Get
                Return _iParam
            End Get
        End Property

        Public ReadOnly Property OParam As OParamBase Implements ICommand.OParam
            Get
                Return _oParam
            End Get
        End Property

        Public Property ResponseType As String Implements ICommand.ResponseType
            Get
                Return _responseType
            End Get
            Set(value As String)
                _responseType = value
            End Set
        End Property

        ''' <summary>
        ''' このコマンドを実行するために必要なロール一覧 (空の場合はログイン済みであれば全ロール許可)
        ''' </summary>
        Protected Overridable ReadOnly Property AllowedRoles As String()
            Get
                Return New String() {"Admin", "Approver", "SalesManager", "Engineer", "QualityAssurance", "Viewer"}
            End Get
        End Property

        ''' <summary>
        ''' IParam.LoginUser をもって、ログインユーザがこの Command を利用可能か検証します。
        ''' </summary>
        Public Overridable Sub Validate() Implements ICommand.Validate
            Dim user As CommandLoginUser = _iParam.LoginUser
            _oParam.SessionIdUsed = If(String.IsNullOrWhiteSpace(_iParam.SessionId), "(Sessionなし/Stateless)", _iParam.SessionId)

            If user Is Nothing OrElse String.IsNullOrWhiteSpace(user.UserId) Then
                _oParam.ValidationPassed = False
                _oParam.PermissionCheckResult = $"NG: LoginUser が未設定のためコマンド '{Me.GetType().Name}' を利用できません。"
                Throw New UnauthorizedAccessException(_oParam.PermissionCheckResult)
            End If

            ' 禁止ユーザ (例: guest / blocked) のチェック
            If String.Equals(user.UserId, "blocked", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(user.UserId, "unauthorized", StringComparison.OrdinalIgnoreCase) Then
                _oParam.ValidationPassed = False
                _oParam.AuthorizedUserId = user.UserId
                _oParam.LoginUserSource = user.SourceChannel
                _oParam.PermissionCheckResult = $"NG: ユーザ '{user.UserId}' にはコマンド '{Me.GetType().Name}' の実行権限がありません。"
                Throw New UnauthorizedAccessException(_oParam.PermissionCheckResult)
            End If

            ' ロール権限チェック (AllowedRoles が指定されており、ユーザがロールを持つ場合)
            Dim requiredRoles As String() = AllowedRoles
            If requiredRoles IsNot Nothing AndAlso requiredRoles.Length > 0 AndAlso user.Roles IsNot Nothing AndAlso user.Roles.Count > 0 Then
                Dim hasRole As Boolean = user.Roles.Any(Function(r) requiredRoles.Any(Function(req) String.Equals(r, req, StringComparison.OrdinalIgnoreCase)))
                If Not hasRole Then
                    _oParam.ValidationPassed = False
                    _oParam.AuthorizedUserId = user.UserId
                    _oParam.LoginUserSource = user.SourceChannel
                    _oParam.PermissionCheckResult = $"NG: ユーザ '{user.UserId}' (Roles={String.Join(",", user.Roles)}) はコマンド '{Me.GetType().Name}' の許可ロール ({String.Join(",", requiredRoles)}) を保持していません。"
                    Throw New UnauthorizedAccessException(_oParam.PermissionCheckResult)
                End If
            End If

            _oParam.ValidationPassed = True
            _oParam.AuthorizedUserId = user.UserId
            _oParam.AuthorizedUserName = user.UserName
            _oParam.LoginUserSource = user.SourceChannel
            _oParam.PermissionCheckResult = $"OK: ユーザ '{user.UserName} ({user.UserId})' [取得元: {user.SourceChannel}] はコマンド '{Me.GetType().Name}' を利用可能です。"
        End Sub

        Public Sub Execute() Implements ICommand.Execute
            If Not _oParam.ValidationPassed Then
                Validate()
            End If
            ExecuteCore()
            _oParam.IsSuccess = True
            _oParam.ExecutedAtUtc = DateTime.UtcNow
        End Sub

        Protected MustOverride Sub ExecuteCore()
    End Class

    ' ====================================================================================
    ' 1. 品目検索コマンド (ItemSearchCommand : Implements ICommand)
    '    主に ClientController (Windows Forms) および ViewController から利用
    ' ====================================================================================
    Public Class ItemSearchIParam
        Inherits IParamBase

        Public Property ConfiguredProvider As String = "SqlServer"
        Public Property ConnectionString As String = String.Empty
        Public Property Keyword As String = String.Empty
    End Class

    Public Class ItemSearchOParam
        Inherits OParamBase

        Public Property Items As New List(Of ItmTblEntity)()
        Public Property ProviderStatus As String = String.Empty
    End Class

    Public Class ItemSearchCommand
        Inherits CommandBase(Of ItemSearchIParam, ItemSearchOParam)

        Public Sub New(Optional iParam As ItemSearchIParam = Nothing)
            MyBase.New(iParam)
            Me.ResponseType = "application/json"
        End Sub

        Protected Overrides Sub ExecuteCore()
            Dim queryCmd As New LegacyItemQueryCommand()
            Dim result As LegacyItemQueryResult = queryCmd.Execute(_iParam.ConfiguredProvider, _iParam.ConnectionString, _iParam.Keyword)
            _oParam.Items = result.Items
            _oParam.ProviderStatus = result.ProviderStatus
        End Sub
    End Class

    ' ====================================================================================
    ' 2. 品目登録・更新コマンド (ItemSaveCommand : Implements ICommand)
    '    主に ClientController (Windows Forms) から利用
    ' ====================================================================================
    Public Class ItemSaveIParam
        Inherits IParamBase

        Public Property ConfiguredProvider As String = "SqlServer"
        Public Property ConnectionString As String = String.Empty
        Public Property ItmCd As String = String.Empty
        Public Property ItmNm As String = String.Empty
        Public Property Category As String = String.Empty
        Public Property UnitPrice As Decimal = 0D
        Public Property StockQty As Integer = 0
    End Class

    Public Class ItemSaveOParam
        Inherits OParamBase

        Public Property SavedItem As ItmTblEntity
    End Class

    Public Class ItemSaveCommand
        Inherits CommandBase(Of ItemSaveIParam, ItemSaveOParam)

        Public Sub New(Optional iParam As ItemSaveIParam = Nothing)
            MyBase.New(iParam)
            Me.ResponseType = "application/json"
        End Sub

        Public Overrides Sub Validate()
            MyBase.Validate()
            If String.IsNullOrWhiteSpace(_iParam.ItmCd) OrElse String.IsNullOrWhiteSpace(_iParam.ItmNm) Then
                Throw New ArgumentException("品目コード (ItmCd) および品目名称 (ItmNm) は必須です。")
            End If
        End Sub

        Protected Overrides Sub ExecuteCore()
            Dim queryCmd As New LegacyItemQueryCommand()
            _oParam.SavedItem = queryCmd.RegisterOrUpdate(
                _iParam.ConfiguredProvider,
                _iParam.ConnectionString,
                _iParam.ItmCd,
                _iParam.ItmNm,
                _iParam.Category,
                _iParam.UnitPrice,
                _iParam.StockQty)
        End Sub
    End Class

    ' ====================================================================================
    ' 3. 図面検索コマンド (DrawingSearchCommand : Implements ICommand)
    '    主に ViewController (Web ブラウザ) から利用
    ' ====================================================================================
    Public Class DrawingSearchIParam
        Inherits IParamBase

        Public Property Keyword As String = String.Empty
    End Class

    Public Class DrawingSearchOParam
        Inherits OParamBase

        Public Property Drawings As New List(Of DrawingRecord)()
    End Class

    Public Class DrawingSearchCommand
        Inherits CommandBase(Of DrawingSearchIParam, DrawingSearchOParam)

        Public Sub New(Optional iParam As DrawingSearchIParam = Nothing)
            MyBase.New(iParam)
            Me.ResponseType = "application/json"
        End Sub

        Protected Overrides Sub ExecuteCore()
            Dim crud As New LegacyDrawingCrudCommand()
            _oParam.Drawings = crud.Search(_iParam.Keyword)
        End Sub
    End Class

    ' ====================================================================================
    ' 4. 図面登録・更新コマンド (DrawingSaveCommand : Implements ICommand)
    '    主に ViewController (Web ブラウザ) から利用
    ' ====================================================================================
    Public Class DrawingSaveIParam
        Inherits IParamBase

        Public Property DrawingNo As String = String.Empty
        Public Property Title As String = String.Empty
        Public Property ItmCd As String = String.Empty
        Public Property BomNo As String = String.Empty
        Public Property Revision As String = "Rev.A"
    End Class

    Public Class DrawingSaveOParam
        Inherits OParamBase

        Public Property SavedDrawing As DrawingRecord
    End Class

    Public Class DrawingSaveCommand
        Inherits CommandBase(Of DrawingSaveIParam, DrawingSaveOParam)

        Public Sub New(Optional iParam As DrawingSaveIParam = Nothing)
            MyBase.New(iParam)
            Me.ResponseType = "application/json"
        End Sub

        Public Overrides Sub Validate()
            MyBase.Validate()
            If String.IsNullOrWhiteSpace(_iParam.DrawingNo) OrElse String.IsNullOrWhiteSpace(_iParam.Title) Then
                Throw New ArgumentException("図面番号 (DrawingNo) および図面名称 (Title) は必須です。")
            End If
        End Sub

        Protected Overrides Sub ExecuteCore()
            Dim crud As New LegacyDrawingCrudCommand()
            _oParam.SavedDrawing = crud.RegisterOrUpdate(
                _iParam.DrawingNo,
                _iParam.Title,
                _iParam.ItmCd,
                _iParam.BomNo,
                _iParam.Revision)
        End Sub
    End Class

    ' ====================================================================================
    ' 5. 顧客照会コマンド (CustomerQueryCommand : Implements ICommand)
    '    主に ClientController (Windows Forms) から利用
    ' ====================================================================================
    Public Class CustomerQueryIParam
        Inherits IParamBase

        Public Property CustomerId As Integer = 1
    End Class

    Public Class CustomerQueryOParam
        Inherits OParamBase

        Public Property CustomerId As Integer
        Public Property CustomerName As String = String.Empty
    End Class

    Public Class CustomerQueryCommand
        Inherits CommandBase(Of CustomerQueryIParam, CustomerQueryOParam)

        Public Sub New(Optional iParam As CustomerQueryIParam = Nothing)
            MyBase.New(iParam)
            Me.ResponseType = "application/json"
        End Sub

        Protected Overrides Sub ExecuteCore()
            _oParam.CustomerId = _iParam.CustomerId
            _oParam.CustomerName = $"株式会社レガシー商事 (ID: {_iParam.CustomerId})"
        End Sub
    End Class

End Namespace
