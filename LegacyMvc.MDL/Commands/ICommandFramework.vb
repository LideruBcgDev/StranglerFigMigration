Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports Common.APP
Imports LegacyMvc.MDL.Data
Imports LegacyMvc.PRM
Imports Newtonsoft.Json.Linq

' ====================================================================================
' Config 指定例: <command name="ITM.APP" command="LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL" db="1" />
' に完全一致するクラス名 (RootNamespace = LegacyMvc.MDL 直下の CommandsLegacyApprovalStore)
' コマンド名には Common.APP.CommandNames.ItmApp 定数を使用
' ====================================================================================
<CommandDefinition(CommandNames.ItmApp, Db:=1, Description:="品目・BOM・図面の承認・照会コマンド (LegacyMvc.MDL.CommandsLegacyApprovalStore)")>
Public Class CommandsLegacyApprovalStore
    Inherits Commands.CommandBase(Of ItemAppIParam, ItemAppOParam)

    Public Sub New()
        MyBase.New(Nothing)
    End Sub

    Public Sub New(iParam As ItemAppIParam)
        MyBase.New(iParam)
    End Sub

    Protected Overrides Sub ExecuteCore()
        Dim queryCmd As New LegacyItemQueryCommand()
        Dim itemRes As LegacyItemQueryResult = queryCmd.Execute(_iParam.ConfiguredProvider, _iParam.ConnectionString, _iParam.TargetItmCd)

        If String.Equals(_iParam.ActionMode, "QUERY_AND_APPROVE", StringComparison.OrdinalIgnoreCase) Then
            Using tx As New Commands.SharedTransactionContext()
                Dim uid As String = If(_iParam.LoginUser?.UserId, "yamada")
                Dim uname As String = If(_iParam.LoginUser?.UserName, "山田 太郎")
                Dim uorg As String = If(_iParam.LoginUser?.OrganizationName, "第一営業本部")

                queryCmd.ApproveItemInTransaction(tx, _iParam.ConfiguredProvider, _iParam.ConnectionString, _iParam.TargetItmCd, uid, uname)

                Dim bomCmd As New Commands.BomApprovalCommand()
                bomCmd.Execute(tx, _iParam.TargetBomNo, uid, uname, uorg)

                Dim drwCmd As New Commands.DrawingApprovalCommand()
                drwCmd.Execute(tx, _iParam.TargetDrawingNo, _iParam.TargetBomNo, uid, uname, uorg)

                tx.Commit()
            End Using

            itemRes = queryCmd.Execute(_iParam.ConfiguredProvider, _iParam.ConnectionString, Nothing)
        End If

        _oParam.Items = itemRes.Items
        _oParam.Boms = Commands.LegacyApprovalStore.GetAllBoms()
        _oParam.Drawings = Commands.LegacyApprovalStore.GetAllDrawings()
        _oParam.ProviderStatus = itemRes.ProviderStatus
        _oParam.SummaryMessage = $"[ITM.APP: CommandsLegacyApprovalStore (db={_iParam.DbNumber})] 品目 {itemRes.Items.Count}件 / BOM {_oParam.Boms.Count}件 / 図面 {_oParam.Drawings.Count}件 を処理しました。"
    End Sub
End Class

Namespace Commands

    ''' <summary>
    ''' コマンド利用可否 (認可) と共通実行フローを提供する抽象基底クラス。
    ''' ICommand / IParamBase / OParamBase は LegacyMvc.PRM プロジェクトに定義されています。
    ''' </summary>
    Public MustInherit Class CommandBase(Of TIParam As {IParamBase, New}, TOParam As {OParamBase, New})
        Implements ICommand

        Protected _iParam As TIParam
        Protected ReadOnly _oParam As TOParam
        Private _responseType As String = "application/json"

        Protected Sub New(Optional iParam As TIParam = Nothing)
            _iParam = If(iParam, New TIParam())
            _oParam = New TOParam() With {
                .ExecutedCommandName = Me.GetType().FullName
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
        ''' JSON 等からデシリアライズされた IParam オブジェクトをコマンドに注入します。
        ''' </summary>
        Public Sub ApplyIParamJson(jToken As JToken, dbNumber As Integer, commandKey As String)
            If jToken IsNot Nothing AndAlso jToken.Type = JTokenType.Object Then
                Dim restored As TIParam = jToken.ToObject(Of TIParam)()
                If restored IsNot Nothing Then
                    _iParam = restored
                End If
            End If
            _iParam.DbNumber = dbNumber
            _iParam.CommandId = commandKey
            _oParam.DbNumberUsed = dbNumber
            _oParam.ExecutedCommandKey = commandKey
        End Sub

        Protected Overridable ReadOnly Property AllowedRoles As String()
            Get
                Return New String() {"Admin", "Approver", "SalesManager", "Engineer", "QualityAssurance", "Viewer"}
            End Get
        End Property

        ''' <summary>
        ''' IParam.LoginUser (Common.APP.LoginUser) をもって、ログインユーザがこの Command を利用可能か検証します。
        ''' </summary>
        Public Overridable Sub Validate() Implements ICommand.Validate
            Dim user As LoginUser = _iParam.LoginUser
            _oParam.SessionIdUsed = If(String.IsNullOrWhiteSpace(_iParam.SessionId), "(Sessionなし/Stateless)", _iParam.SessionId)
            _oParam.DbNumberUsed = _iParam.DbNumber

            If user Is Nothing OrElse String.IsNullOrWhiteSpace(user.UserId) Then
                _oParam.ValidationPassed = False
                _oParam.PermissionCheckResult = $"NG: LoginUser が未設定のためコマンド '{Me.GetType().Name}' を利用できません。"
                Throw New UnauthorizedAccessException(_oParam.PermissionCheckResult)
            End If

            If String.Equals(user.UserId, "blocked", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(user.UserId, "unauthorized", StringComparison.OrdinalIgnoreCase) Then
                _oParam.ValidationPassed = False
                _oParam.AuthorizedUserId = user.UserId
                _oParam.LoginUserSource = user.SourceChannel
                _oParam.PermissionCheckResult = $"NG: ユーザ '{user.UserId}' にはコマンド '{Me.GetType().Name}' の実行権限がありません。"
                Throw New UnauthorizedAccessException(_oParam.PermissionCheckResult)
            End If

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
            _oParam.PermissionCheckResult = $"OK: ユーザ '{user.UserName} ({user.UserId})' [取得元: {user.SourceChannel}] はコマンド '{Me.GetType().Name}' (db={_iParam.DbNumber}) を利用可能です。"
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
    ' 具体コマンドクラス群 (ICommand 実装 / LegacyMvc.PRM の IParam・OParam を使用)
    ' ====================================================================================

    <CommandDefinition(CommandNames.ItmSch, Db:=1, Description:="品目検索コマンド (ItemSearchCommand)")>
    Public Class ItemSearchCommand
        Inherits CommandBase(Of ItemSearchIParam, ItemSearchOParam)

        Public Sub New()
            MyBase.New(Nothing)
        End Sub

        Public Sub New(iParam As ItemSearchIParam)
            MyBase.New(iParam)
        End Sub

        Protected Overrides Sub ExecuteCore()
            Dim queryCmd As New LegacyItemQueryCommand()
            Dim result As LegacyItemQueryResult = queryCmd.Execute(_iParam.ConfiguredProvider, _iParam.ConnectionString, _iParam.Keyword)
            _oParam.Items = result.Items
            _oParam.ProviderStatus = result.ProviderStatus
        End Sub
    End Class

    <CommandDefinition(CommandNames.ItmSav, Db:=1, Description:="品目登録・更新コマンド (ItemSaveCommand)")>
    Public Class ItemSaveCommand
        Inherits CommandBase(Of ItemSaveIParam, ItemSaveOParam)

        Public Sub New()
            MyBase.New(Nothing)
        End Sub

        Public Sub New(iParam As ItemSaveIParam)
            MyBase.New(iParam)
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

    <CommandDefinition(CommandNames.DrwSch, Db:=1, Description:="図面検索コマンド (DrawingSearchCommand)")>
    Public Class DrawingSearchCommand
        Inherits CommandBase(Of DrawingSearchIParam, DrawingSearchOParam)

        Public Sub New()
            MyBase.New(Nothing)
        End Sub

        Public Sub New(iParam As DrawingSearchIParam)
            MyBase.New(iParam)
        End Sub

        Protected Overrides Sub ExecuteCore()
            Dim crud As New LegacyDrawingCrudCommand()
            _oParam.Drawings = crud.Search(_iParam.Keyword)
        End Sub
    End Class

    <CommandDefinition(CommandNames.DrwSav, Db:=1, Description:="図面登録・更新コマンド (DrawingSaveCommand)")>
    Public Class DrawingSaveCommand
        Inherits CommandBase(Of DrawingSaveIParam, DrawingSaveOParam)

        Public Sub New()
            MyBase.New(Nothing)
        End Sub

        Public Sub New(iParam As DrawingSaveIParam)
            MyBase.New(iParam)
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

    <CommandDefinition(CommandNames.CusSch, Db:=1, Description:="顧客照会コマンド (CustomerQueryCommand)")>
    Public Class CustomerQueryCommand
        Inherits CommandBase(Of CustomerQueryIParam, CustomerQueryOParam)

        Public Sub New()
            MyBase.New(Nothing)
        End Sub

        Public Sub New(iParam As CustomerQueryIParam)
            MyBase.New(iParam)
        End Sub

        Protected Overrides Sub ExecuteCore()
            _oParam.CustomerId = _iParam.CustomerId
            _oParam.CustomerName = $"株式会社レガシー商事 (ID: {_iParam.CustomerId})"
        End Sub
    End Class

    ' ====================================================================================
    ' コマンドディスパッチャ (CommandDispatcher)
    ' 1. レガシー方式: <command name="ITM.APP" command="LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL" db="1" />
    '    の Config 定義から Type.GetType + Activator.CreateInstance で ICommand を生成
    ' 2. 改善別案: <CommandDefinition("ITM.APP", Db:=1)> 属性から自動スキャン＆キャッシュして生成
    ' ====================================================================================
    Public Class CommandConfigEntry
        Public Property Name As String = String.Empty
        Public Property CommandTypeString As String = String.Empty
        Public Property Db As Integer = 1
        Public Property RegistrationSource As String = "Config (<command name=... />)"
    End Class

    Public NotInheritable Class CommandDispatcher
        Private Shared ReadOnly ConfigMappings As New Dictionary(Of String, CommandConfigEntry)(StringComparer.OrdinalIgnoreCase)

        Shared Sub New()
            LegacyCommands.EnsureRegistered()
            ' 1. Config (<command name="ITM.APP" command="LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL" db="1" />) の既定登録 (CommandNames 定数を使用)
            RegisterConfigCommand(CommandNames.ItmApp, "LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL", 1, "Config (<command name=""ITM.APP"" command=""LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL"" db=""1"">)")
            RegisterConfigCommand(CommandNames.ItmSch, "LegacyMvc.MDL.Commands.ItemSearchCommand,LegacyMvc.MDL", 1, "Config (<command name=""ITM.SCH"" ... db=""1"">)")
            RegisterConfigCommand(CommandNames.ItmSav, "LegacyMvc.MDL.Commands.ItemSaveCommand,LegacyMvc.MDL", 1, "Config (<command name=""ITM.SAV"" ... db=""1"">)")
            RegisterConfigCommand(CommandNames.DrwSch, "LegacyMvc.MDL.Commands.DrawingSearchCommand,LegacyMvc.MDL", 1, "Config (<command name=""DRW.SCH"" ... db=""1"">)")
            RegisterConfigCommand(CommandNames.DrwSav, "LegacyMvc.MDL.Commands.DrawingSaveCommand,LegacyMvc.MDL", 1, "Config (<command name=""DRW.SAV"" ... db=""1"">)")
            RegisterConfigCommand(CommandNames.CusSch, "LegacyMvc.MDL.Commands.CustomerQueryCommand,LegacyMvc.MDL", 1, "Config (<command name=""CUS.SCH"" ... db=""1"">)")

            ' 2. 別案: <CommandDefinition> 属性によるアセンブリ自動検出 (Config 記述漏れ防止)
            RegisterAssembly(GetType(CommandDispatcher).Assembly)
        End Sub

        Private Sub New()
        End Sub

        ''' <summary>
        ''' 指定されたアセンブリ (LegacyMvc.MDL や ModernBff.MDL) 内の &lt;CommandDefinition&gt; 属性付き ICommand クラスを自動登録します。
        ''' </summary>
        Public Shared Sub RegisterAssembly(asm As Assembly)
            If asm Is Nothing Then Return
            For Each t As Type In asm.GetTypes()
                Dim attrs = t.GetCustomAttributes(GetType(CommandDefinitionAttribute), False)
                For Each attrObj As Object In attrs
                    Dim attr As CommandDefinitionAttribute = DirectCast(attrObj, CommandDefinitionAttribute)
                    If Not ConfigMappings.ContainsKey(attr.Name) Then
                        RegisterConfigCommand(attr.Name, $"{t.FullName},{t.Assembly.GetName().Name}", attr.Db, $"Attribute Auto-Discovery ({t.Assembly.GetName().Name}: <CommandDefinition(""{attr.Name}"")>)")
                    End If
                Next
            Next
        End Sub

        Public Shared Sub RegisterConfigCommand(name As String, commandTypeString As String, db As Integer, Optional source As String = "Config")
            ConfigMappings(name) = New CommandConfigEntry With {
                .Name = name,
                .CommandTypeString = commandTypeString,
                .Db = db,
                .RegistrationSource = source
            }
        End Sub

        Public Shared Function GetRegisteredCommands() As List(Of CommandConfigEntry)
            Return ConfigMappings.Values.OrderBy(Function(c) c.Name).ToList()
        End Function

        ''' <summary>
        ''' コマンド名 (例: "ITM.APP") から Config を引き当て、LegacyMvc.MDL.dll の対象クラスをインスタンス化して返します。
        ''' </summary>
        Public Shared Function CreateCommand(
            commandName As String,
            iParamJson As JToken,
            ByRef resolvedEntry As CommandConfigEntry) As ICommand

            If String.IsNullOrWhiteSpace(commandName) OrElse Not ConfigMappings.TryGetValue(commandName.Trim(), resolvedEntry) Then
                Throw New KeyNotFoundException($"コマンド名 '{commandName}' は Config (<command name=""..."" />) に登録されていません。登録済: {String.Join(", ", ConfigMappings.Keys)}")
            End If

            Dim targetType As Type = Type.GetType(resolvedEntry.CommandTypeString, throwOnError:=False, ignoreCase:=True)
            If targetType Is Nothing Then
                Dim classNameOnly As String = resolvedEntry.CommandTypeString.Split(","c)(0).Trim()
                targetType = GetType(CommandDispatcher).Assembly.GetType(classNameOnly, throwOnError:=False, ignoreCase:=True)
                If targetType Is Nothing Then
                    For Each asm As Assembly In AppDomain.CurrentDomain.GetAssemblies()
                        targetType = asm.GetType(classNameOnly, throwOnError:=False, ignoreCase:=True)
                        If targetType IsNot Nothing Then Exit For
                    Next
                End If
            End If

            If targetType Is Nothing Then
                Throw New TypeLoadException($"Config に指定されたコマンド型 '{resolvedEntry.CommandTypeString}' を LegacyMvc.MDL.dll からロードできませんでした。")
            End If

            Dim instance As Object = Activator.CreateInstance(targetType)
            Dim cmd As ICommand = TryCast(instance, ICommand)
            If cmd Is Nothing Then
                Throw New InvalidOperationException($"型 '{targetType.FullName}' は LegacyMvc.PRM.ICommand を実装していません。")
            End If

            ' ApplyIParamJson メソッドがあれば呼び出して JSON から具体 IParam 型を復元
            Dim applyMethod As MethodInfo = targetType.GetMethod("ApplyIParamJson", BindingFlags.Instance Or BindingFlags.Public)
            If applyMethod IsNot Nothing Then
                applyMethod.Invoke(cmd, New Object() {iParamJson, resolvedEntry.Db, resolvedEntry.Name})
            Else
                cmd.IParam.DbNumber = resolvedEntry.Db
                cmd.IParam.CommandId = resolvedEntry.Name
            End If

            Return cmd
        End Function
    End Class

End Namespace
