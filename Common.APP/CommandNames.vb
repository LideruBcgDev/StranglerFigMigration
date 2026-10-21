Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Reflection

Namespace Global.Common.APP

    ''' <summary>
    ''' 【全プロジェクト共通：コマンド名定数一覧 (Common.APP.CommandNames)】
    ''' ・サーバー側の &lt;CommandDefinition(CommandNames.ItmApp, Db:=1)&gt; 属性や Config 定義
    ''' ・クライアント側 (WinFormsClient) のコマンド呼び出し
    ''' の双方で同じ Public Const 定数を参照することで、コマンド名のタイポや不一致をコンパイル時に防止します。
    ''' </summary>
    Public NotInheritable Class CommandNames
        Private Sub New()
        End Sub

        ' --- LegacyMvc.MDL コマンド名定数 ---
        ''' <summary>品目・BOM・図面承認照会コマンド ("ITM.APP" -> CommandsLegacyApprovalStore)</summary>
        Public Const ItmApp As String = "ITM.APP"

        ''' <summary>品目マスタ検索コマンド ("ITM.SCH" -> ItemSearchCommand)</summary>
        Public Const ItmSch As String = "ITM.SCH"

        ''' <summary>品目マスタ登録・更新コマンド ("ITM.SAV" -> ItemSaveCommand)</summary>
        Public Const ItmSav As String = "ITM.SAV"

        ''' <summary>品目マスタ複製コマンド ("ITM.CPY" -> ItemDuplicateCommand)</summary>
        Public Const ItmCpy As String = "ITM.CPY"

        ''' <summary>品目マスタ改訂コマンド ("ITM.REV" -> ItemReviseCommand)</summary>
        Public Const ItmRev As String = "ITM.REV"

        ''' <summary>図面マスタ検索コマンド ("DRW.SCH" -> DrawingSearchCommand)</summary>
        Public Const DrwSch As String = "DRW.SCH"

        ''' <summary>図面マスタ登録・更新コマンド ("DRW.SAV" -> DrawingSaveCommand)</summary>
        Public Const DrwSav As String = "DRW.SAV"

        ''' <summary>顧客情報照会コマンド ("CUS.SCH" -> CustomerQueryCommand)</summary>
        Public Const CusSch As String = "CUS.SCH"

        ''' <summary>図面・品目ポータル Web 画面起動コマンド ("DRW.PORTAL" -> LegacyPortal.aspx リダイレクト)</summary>
        Public Const DrwPortal As String = "DRW.PORTAL"

        ' --- ModernBff.MDL コマンド名定数 ---
        ''' <summary>案件統合検索コマンド ("PRJ.SCH" -> ProjectSearchCommand)</summary>
        Public Const PrjSch As String = "PRJ.SCH"

        ''' <summary>案件統合登録・更新コマンド ("PRJ.SAV" -> ProjectSaveCommand)</summary>
        Public Const PrjSav As String = "PRJ.SAV"

        ''' <summary>ワークフロー申請コマンド ("WF.APP" -> WorkflowApplyCommand)</summary>
        Public Const WfApp As String = "WF.APP"

        ''' <summary>ワークフロー照査コマンド ("WF.VRF" -> WorkflowVerifyCommand)</summary>
        Public Const WfVrf As String = "WF.VRF"

        ''' <summary>ワークフロー最終承認コマンド ("WF.APR" -> WorkflowApproveCommand)</summary>
        Public Const WfApr As String = "WF.APR"
    End Class

    ''' <summary>
    ''' コマンド名 (Name) と IParam 型・OParam 型の対応ペア情報
    ''' </summary>
    Public Class CommandParamPair
        Public ReadOnly Property CommandName As String
        Public ReadOnly Property IParamType As Type
        Public ReadOnly Property OParamType As Type

        Public Sub New(commandName As String, iParamType As Type, oParamType As Type)
            Me.CommandName = commandName
            Me.IParamType = iParamType
            Me.OParamType = oParamType
        End Sub
    End Class

    ''' <summary>
    ''' 【コマンド名 ⇔ (IParam型, OParam型) レジストリ (Common.APP.CommandParamRegistry)】
    ''' コマンド名 (例: CommandNames.ItmApp) が決まれば IParam (ItemAppIParam) と OParam (ItemAppOParam) の型が一意に決まるため、
    ''' その対応関係を管理し、以下を提供します:
    ''' 1. CreateIParam(commandName): 各画面で直接 New ItemAppIParam() を書かずにコマンド名から正しい IParam を生成
    ''' 2. ValidateIParamType(commandName, iParam): 誤った IParam 型が渡された場合に通信前に即座に検出
    ''' 3. GetOParamType(commandName): 返却された JSON を確実に正しい OParam 型へ復元
    ''' </summary>
    Public NotInheritable Class CommandParamRegistry
        Private Shared ReadOnly Pairs As New ConcurrentDictionary(Of String, CommandParamPair)(StringComparer.OrdinalIgnoreCase)

        Private Sub New()
        End Sub

        Public Shared Sub Register(commandName As String, iParamType As Type, oParamType As Type)
            If String.IsNullOrWhiteSpace(commandName) OrElse iParamType Is Nothing OrElse oParamType Is Nothing Then Return
            Pairs(commandName.Trim()) = New CommandParamPair(commandName.Trim(), iParamType, oParamType)
        End Sub

        Public Shared Function TryGetPair(commandName As String, ByRef pair As CommandParamPair) As Boolean
            If String.IsNullOrWhiteSpace(commandName) Then
                pair = Nothing
                Return False
            End If
            Return Pairs.TryGetValue(commandName.Trim(), pair)
        End Function

        Public Shared Function GetOParamType(commandName As String) As Type
            Dim pair As CommandParamPair = Nothing
            If TryGetPair(commandName, pair) Then
                Return pair.OParamType
            End If
            Return Nothing
        End Function

        Public Shared Function GetIParamType(commandName As String) As Type
            Dim pair As CommandParamPair = Nothing
            If TryGetPair(commandName, pair) Then
                Return pair.IParamType
            End If
            Return Nothing
        End Function

        ''' <summary>
        ''' コマンド名定数 (例: CommandNames.ItmApp) から対応する IParam インスタンスを自動生成します。
        ''' </summary>
        Public Shared Function CreateIParam(commandName As String) As Object
            Dim pair As CommandParamPair = Nothing
            If Not TryGetPair(commandName, pair) Then
                Throw New KeyNotFoundException($"コマンド名 '{commandName}' に対応する IParam/OParam 型が CommandParamRegistry に登録されていません。")
            End If

            Dim instance As Object = Activator.CreateInstance(pair.IParamType)
            Dim cmdIdProp As PropertyInfo = pair.IParamType.GetProperty("CommandId")
            If cmdIdProp IsNot Nothing AndAlso cmdIdProp.CanWrite Then
                cmdIdProp.SetValue(instance, pair.CommandName)
            End If
            Return instance
        End Function

        ''' <summary>
        ''' コマンド名定数から対応する IParam インスタンスを生成し、指定された TIParam 型として返します。
        ''' 万一コマンド名と TIParam の型が一致しない場合は即座に InvalidCastException をスローして型変換ミスを防ぎます。
        ''' </summary>
        Public Shared Function CreateIParam(Of TIParam As Class)(commandName As String) As TIParam
            Dim raw As Object = CreateIParam(commandName)
            Dim typed As TIParam = TryCast(raw, TIParam)
            If typed Is Nothing Then
                Throw New InvalidCastException(
                    $"コマンド名 '{commandName}' の本来の IParam 型は '{raw.GetType().FullName}' ですが、誤って '{GetType(TIParam).FullName}' として生成しようとしました。")
            End If
            Return typed
        End Function

        ''' <summary>
        ''' 送信前にコマンド名と IParam インスタンスの型が一致しているかを検証し、不一致の場合は例外をスローします。
        ''' </summary>
        Public Shared Sub ValidateIParamType(commandName As String, iParam As Object)
            If iParam Is Nothing Then Return
            Dim pair As CommandParamPair = Nothing
            If TryGetPair(commandName, pair) Then
                If Not pair.IParamType.IsAssignableFrom(iParam.GetType()) Then
                    Throw New InvalidCastException(
                        $"[型不一致エラー] コマンド '{commandName}' には '{pair.IParamType.FullName}' を渡す必要がありますが、'{iParam.GetType().FullName}' が渡されました。")
                End If
            End If
        End Sub

        Public Shared Function GetAllRegisteredPairs() As IReadOnlyCollection(Of CommandParamPair)
            Return DirectCast(Pairs.Values, IReadOnlyCollection(Of CommandParamPair))
        End Function
    End Class

    ''' <summary>
    ''' 【型安全なコマンド定義スペック (Common.APP.CommandSpec(Of TIParam, TOParam))】
    ''' コマンド名定数 (CommandNames.XXX) と TIParam 型・TOParam 型を1つのオブジェクトとして束ねます。
    ''' これを使うことで:
    ''' 1. spec.CreateIParam() で New XXXIParam() を直接書かずに正しい IParam を生成可能
    ''' 2. HttpUtil.ExecuteCommandAsync(spec, Sub(p) ...) で DirectCast すら手書き不要（コンパイル時に IParam と OParam の型が100%保証される）
    ''' </summary>
    Public Class CommandSpec(Of TIParam As {Class, New}, TOParam As Class)
        Public ReadOnly Property Name As String
        Public ReadOnly Property IParamType As Type
        Public ReadOnly Property OParamType As Type

        Public Sub New(name As String)
            Me.Name = name
            Me.IParamType = GetType(TIParam)
            Me.OParamType = GetType(TOParam)
            CommandParamRegistry.Register(name, Me.IParamType, Me.OParamType)
        End Sub

        ''' <summary>
        ''' このコマンド専用の IParam インスタンス (TIParam) を生成し、CommandId にコマンド名定数を自動セットして返します。
        ''' 各画面で New ItemAppIParam() を手書きする必要がなくなります。
        ''' </summary>
        Public Function CreateIParam(Optional configure As Action(Of TIParam) = Nothing) As TIParam
            Dim param As New TIParam()
            Dim cmdIdProp As PropertyInfo = GetType(TIParam).GetProperty("CommandId")
            If cmdIdProp IsNot Nothing AndAlso cmdIdProp.CanWrite Then
                cmdIdProp.SetValue(param, Me.Name)
            End If
            If configure IsNot Nothing Then
                configure.Invoke(param)
            End If
            Return param
        End Function
    End Class

End Namespace
