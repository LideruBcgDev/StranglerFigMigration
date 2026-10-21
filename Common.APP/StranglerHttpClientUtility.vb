Imports System
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Reflection
Imports System.Text
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Namespace Global.Common.APP

    ''' <summary>
    ''' コマンド実行要求の共通エンベロープ (ClientController / ViewController 宛て)
    ''' </summary>
    Public Class CommandDispatchRequest
        <JsonProperty("commandName")>
        Public Property CommandName As String = String.Empty

        <JsonProperty("iParamType")>
        Public Property IParamType As String = String.Empty

        <JsonProperty("iParam")>
        Public Property IParam As Object

        ''' <summary>BinaryFormatter でシリアライズされた byte[] の Base64 文字列 (Interface型プロパティ完全保持用)</summary>
        <JsonProperty("payloadBase64")>
        Public Property PayloadBase64 As String = String.Empty

        <JsonProperty("isBinaryFormatted")>
        Public Property IsBinaryFormatted As Boolean = False

        <JsonProperty("useJsonWithType")>
        Public Property UseJsonWithType As Boolean = False
    End Class

    ''' <summary>
    ''' 【全プロジェクト共通 HTTP 通信ユーティリティ (Common.APP.StranglerHttpClientUtility)】
    ''' WinFormsClient 等から YarpGateway / ModernBff / LegacyMvc と通信するための共通ユーティリティです。
    ''' ・Cookie / X-Strangler-AuthToken による共通 LoginUser セッション保持
    ''' ・/oauth/token で取得した Bearer access_token の保持
    ''' ・コマンド名 (例: "ITM.APP", "ITM.SCH") を指定した ClientController / ViewController 呼び出しと、
    '''   返却された OParam を LegacyMvc.PRM の具体クラスへ DirectCast 可能な形で復元する機能を提供します。
    ''' </summary>
    Public Class StranglerHttpClientUtility
        Public ReadOnly Property GatewayBaseUrl As String
        Public ReadOnly Property CookieContainer As CookieContainer
        Public ReadOnly Property HttpClient As HttpClient

        Private _currentLoginUser As LoginUser
        Private _currentSessionToken As String = String.Empty
        Private _currentBearerToken As String = String.Empty

        Public Sub New(Optional baseUrl As String = Nothing)
            Me.GatewayBaseUrl = If(Not String.IsNullOrWhiteSpace(baseUrl), EnsureTrailingSlash(baseUrl), LoadGatewayBaseUrlFromSettings())
            Me.CookieContainer = New CookieContainer()

            Dim handler As New HttpClientHandler() With {
                .CookieContainer = Me.CookieContainer,
                .UseCookies = True
            }

            Me.HttpClient = New HttpClient(handler) With {
                .BaseAddress = New Uri(Me.GatewayBaseUrl),
                .Timeout = TimeSpan.FromSeconds(15)
            }
        End Sub

        Public ReadOnly Property CurrentLoginUser As LoginUser
            Get
                Return _currentLoginUser
            End Get
        End Property

        Public ReadOnly Property CurrentSessionToken As String
            Get
                Return _currentSessionToken
            End Get
        End Property

        Public ReadOnly Property SessionToken As String
            Get
                Return _currentSessionToken
            End Get
        End Property

        Public ReadOnly Property CurrentBearerToken As String
            Get
                Return _currentBearerToken
            End Get
        End Property

        Public ReadOnly Property BearerAccessToken As String
            Get
                Return _currentBearerToken
            End Get
        End Property

        ''' <summary>
        ''' Web ログイン成功時に取得したセッショントークンと LoginUser を共通 HttpClient に設定します。
        ''' </summary>
        Public Sub SetLoginSession(sessionToken As String, Optional user As LoginUser = Nothing)
            If user IsNot Nothing Then
                user.SessionToken = sessionToken
                SetAuthenticatedUser(user)
            Else
                _currentSessionToken = If(sessionToken, String.Empty)
                Me.HttpClient.DefaultRequestHeaders.Remove(LoginUser.HeaderName)
                If Not String.IsNullOrWhiteSpace(_currentSessionToken) Then
                    Me.HttpClient.DefaultRequestHeaders.Add(LoginUser.HeaderName, _currentSessionToken)
                    Me.CookieContainer.Add(New Uri(Me.GatewayBaseUrl), New Cookie(LoginUser.CookieName, _currentSessionToken, "/"))
                End If
            End If
        End Sub

        ''' <summary>
        ''' Web ログイン成功時に取得した LoginUser とセッショントークンを共通 HttpClient に設定します。
        ''' </summary>
        Public Sub SetAuthenticatedUser(user As LoginUser)
            _currentLoginUser = user
            _currentSessionToken = If(user?.SessionToken, String.Empty)

            Me.HttpClient.DefaultRequestHeaders.Remove(LoginUser.HeaderName)
            If Not String.IsNullOrWhiteSpace(_currentSessionToken) Then
                Me.HttpClient.DefaultRequestHeaders.Add(LoginUser.HeaderName, _currentSessionToken)
                Me.CookieContainer.Add(New Uri(Me.GatewayBaseUrl), New Cookie(LoginUser.CookieName, _currentSessionToken, "/"))
            End If
        End Sub

        ''' <summary>
        ''' ログアウト時にセッショントークンと LoginUser をクリアします。
        ''' </summary>
        Public Sub ClearSession()
            _currentLoginUser = Nothing
            _currentSessionToken = String.Empty
            Me.HttpClient.DefaultRequestHeaders.Remove(LoginUser.HeaderName)
        End Sub

        ''' <summary>
        ''' REST API (/api/.*) 呼び出し用の OAuth 2.0 Bearer access_token を設定します。
        ''' </summary>
        Public Sub SetBearerAccessToken(bearerToken As String)
            _currentBearerToken = If(bearerToken, String.Empty)
            If Not String.IsNullOrWhiteSpace(_currentBearerToken) Then
                Me.HttpClient.DefaultRequestHeaders.Authorization = New AuthenticationHeaderValue("Bearer", _currentBearerToken)
            Else
                Me.HttpClient.DefaultRequestHeaders.Authorization = Nothing
            End If
        End Sub

        ''' <summary>
        ''' GET リクエストを送信します。
        ''' </summary>
        Public Function GetAsync(relativeUrl As String) As Task(Of HttpResponseMessage)
            Return Me.HttpClient.GetAsync(relativeUrl.TrimStart("/"c))
        End Function

        ''' <summary>
        ''' 任意のオブジェクトを JSON シリアライズして POST 送信します。
        ''' </summary>
        Public Function PostJsonAsync(relativeUrl As String, payload As Object) As Task(Of HttpResponseMessage)
            Dim json As String = JsonConvert.SerializeObject(payload)
            Dim content As New StringContent(json, Encoding.UTF8, "application/json")
            Return Me.HttpClient.PostAsync(relativeUrl.TrimStart("/"c), content)
        End Function

        ''' <summary>
        ''' 【推奨1：型安全な CommandSpec + ラムダ式によるコマンド実行】
        ''' 各画面で New ItemAppIParam() や DirectCast(..., ItemAppOParam) を手書きする必要がなく、
        ''' コマンド定義 (例: LegacyCommands.ItmApp / ModernCommands.PrjSch) を指定するだけで
        ''' 正しい IParam の自動生成と OParam 型での返却がコンパイル時に100%保証されます。
        ''' </summary>
        Public Async Function ExecuteCommandAsync(Of TIParam As {Class, New}, TOParam As Class)(
            spec As CommandSpec(Of TIParam, TOParam),
            Optional configureIParam As Action(Of TIParam) = Nothing,
            Optional endpointPath As String = "Client/execute",
            Optional useBinaryFormatter As Boolean = False) As Task(Of TOParam)

            If spec Is Nothing Then Throw New ArgumentNullException(NameOf(spec))
            Dim iParam As TIParam = spec.CreateIParam(configureIParam)
            Dim rawOParam As Object = Await ExecuteCommandAsync(spec.Name, CObj(iParam), endpointPath, useBinaryFormatter)
            Return DirectCast(rawOParam, TOParam)
        End Function

        ''' <summary>
        ''' 【推奨2：型安全な CommandSpec + 生成済み TIParam によるコマンド実行】
        ''' spec.CreateIParam() で生成した IParam を渡し、型変換ミスなく TOParam を取得します。
        ''' </summary>
        Public Async Function ExecuteCommandAsync(Of TIParam As {Class, New}, TOParam As Class)(
            spec As CommandSpec(Of TIParam, TOParam),
            iParam As TIParam,
            Optional endpointPath As String = "Client/execute",
            Optional useBinaryFormatter As Boolean = False) As Task(Of TOParam)

            If spec Is Nothing Then Throw New ArgumentNullException(NameOf(spec))
            Dim effectiveIParam As TIParam = If(iParam, spec.CreateIParam())
            Dim rawOParam As Object = Await ExecuteCommandAsync(spec.Name, CObj(effectiveIParam), endpointPath, useBinaryFormatter)
            Return DirectCast(rawOParam, TOParam)
        End Function

        ''' <summary>
        ''' コマンド名定数 (例: CommandNames.ItmApp, CommandNames.ItmSch) と IParam を指定して
        ''' ClientController (/Client/execute) または ViewController (/View/execute) を呼び出し、
        ''' 返却された OParam インスタンスを LegacyMvc.PRM / ModernBff.PRM の具体型として復元して返します。
        ''' useBinaryFormatter:=True の場合、byte[] を Base64 文字列化して JSON に乗せ、Interface 型プロパティを完全保持します。
        ''' </summary>
        Public Async Function ExecuteCommandAsync(
            commandName As String,
            iParam As Object,
            Optional endpointPath As String = "Client/execute",
            Optional useBinaryFormatter As Boolean = False) As Task(Of Object)

            ' コマンド名に対して IParam が未指定の場合は CommandParamRegistry から自動生成
            If iParam Is Nothing AndAlso CommandParamRegistry.GetIParamType(commandName) IsNot Nothing Then
                iParam = CommandParamRegistry.CreateIParam(commandName)
            End If

            ' 送信前に「コマンド名」と「渡された IParam の型」が一致しているか検証 (型間違い防止)
            CommandParamRegistry.ValidateIParamType(commandName, iParam)

            ' IParam に LoginUser プロパティがあり、未設定の場合は現在ログイン中の LoginUser を自動設定
            If iParam IsNot Nothing AndAlso _currentLoginUser IsNot Nothing Then
                Dim loginProp As PropertyInfo = iParam.GetType().GetProperty("LoginUser")
                If loginProp IsNot Nothing AndAlso loginProp.CanWrite Then
                    Dim existingUser As LoginUser = TryCast(loginProp.GetValue(iParam), LoginUser)
                    If existingUser Is Nothing OrElse String.IsNullOrWhiteSpace(existingUser.UserId) Then
                        loginProp.SetValue(iParam, _currentLoginUser)
                    End If
                End If
            End If

            Dim reqEnvelope As New CommandDispatchRequest With {
                .CommandName = commandName,
                .IParamType = If(iParam IsNot Nothing, iParam.GetType().AssemblyQualifiedName, String.Empty),
                .IParam = iParam,
                .IsBinaryFormatted = useBinaryFormatter
            }

            ' BinaryFormatter 互換モードの場合、byte[] を Base64 化して格納
            If useBinaryFormatter AndAlso iParam IsNot Nothing Then
                Try
                    reqEnvelope.PayloadBase64 = BinaryPayloadHelper.SerializeToBase64(iParam)
                Catch
                End Try
            End If

            Dim response As HttpResponseMessage = Await PostJsonAsync(endpointPath, reqEnvelope)
            Dim rawJson As String = Await response.Content.ReadAsStringAsync()

            ' 【Web 画面 (.aspx) 起動リダイレクト判定】
            Dim redirectHeaderUrl As String = Nothing
            If response.Headers.Contains("X-Strangler-Redirect-Url") Then
                redirectHeaderUrl = response.Headers.GetValues("X-Strangler-Redirect-Url").FirstOrDefault()
            End If

            If Not String.IsNullOrWhiteSpace(rawJson) AndAlso rawJson.TrimStart().StartsWith("{") Then
                Dim root As JObject = JObject.Parse(rawJson)
                Dim isRedirectToken As JToken = If(root("isRedirect"), root("IsRedirect"))
                Dim redirectUrlToken As JToken = If(root("redirectUrl"), root("RedirectUrl"))
                Dim reqCodeToken As JToken = If(root("reqCode"), root("ReqCode"))
                Dim aspxToken As JToken = If(root("aspx"), root("Aspx"))

                Dim isRedirect As Boolean = (isRedirectToken IsNot Nothing AndAlso Convert.ToBoolean(isRedirectToken)) OrElse
                                            (redirectUrlToken IsNot Nothing) OrElse
                                            Not String.IsNullOrWhiteSpace(redirectHeaderUrl)

                If isRedirect Then
                    Dim redirectUrl As String = If(redirectUrlToken IsNot Nothing, redirectUrlToken.ToString(), redirectHeaderUrl)
                    Dim reqCode As String = If(reqCodeToken IsNot Nothing, reqCodeToken.ToString(), String.Empty)
                    Dim aspxPage As String = If(aspxToken IsNot Nothing, aspxToken.ToString(), String.Empty)

                    Throw New RedirectException(redirectUrl, reqCode, aspxPage, commandName)
                End If

                ' BinaryFormatter over JSON の場合、サーバーから返された Base64 から OParam を直接復元
                Dim isBinaryResToken As JToken = If(root("isBinaryFormatted"), root("IsBinaryFormatted"))
                Dim payloadBase64Token As JToken = If(root("payloadBase64"), root("PayloadBase64"))
                If (useBinaryFormatter OrElse (isBinaryResToken IsNot Nothing AndAlso Convert.ToBoolean(isBinaryResToken))) AndAlso
                   payloadBase64Token IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(payloadBase64Token.ToString()) Then
                    Dim binaryOParam = BinaryPayloadHelper.DeserializeFromBase64(payloadBase64Token.ToString())
                    If binaryOParam IsNot Nothing Then
                        Return binaryOParam
                    End If
                End If

                Dim oParamTypeToken As JToken = If(root("oParamType"), root("OParamType"))
                Dim oParamToken As JToken = If(root("oParam"), root("OParam"))

                If oParamToken Is Nothing Then
                    Throw New InvalidOperationException($"コマンド '{commandName}' の応答に OParam が含まれていません。Response: {rawJson}")
                End If

                ' 1. まず CommandParamRegistry に登録された OParam 型を優先解決
                Dim targetType As Type = CommandParamRegistry.GetOParamType(commandName)

                ' 2. 未登録の場合はレスポンス内の oParamType 文字列から解決
                If targetType Is Nothing Then
                    Dim typeName As String = If(oParamTypeToken IsNot Nothing, oParamTypeToken.ToString(), String.Empty)
                    targetType = ResolvePrmType(typeName)
                End If

                If targetType IsNot Nothing Then
                    Return oParamToken.ToObject(targetType)
                End If

                Return oParamToken
            End If

            Return Nothing
        End Function

        Private Shared Function ResolvePrmType(typeName As String) As Type
            If String.IsNullOrWhiteSpace(typeName) Then Return Nothing

            Dim resolved As Type = Type.GetType(typeName, throwOnError:=False)
            If resolved IsNot Nothing Then Return resolved

            Dim shortTypeName As String = typeName.Split(","c)(0).Trim()
            For Each asm As Assembly In AppDomain.CurrentDomain.GetAssemblies()
                resolved = asm.GetType(shortTypeName, throwOnError:=False, ignoreCase:=True)
                If resolved IsNot Nothing Then Return resolved
            Next

            Return Nothing
        End Function

        Private Shared Function LoadGatewayBaseUrlFromSettings() As String
            Try
                Dim settingsPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client-settings.json")
                If File.Exists(settingsPath) Then
                    Dim json As String = File.ReadAllText(settingsPath)
                    Dim obj As JObject = JObject.Parse(json)
                    Dim urlToken As JToken = obj("GatewayBaseUrl")
                    If urlToken IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(urlToken.ToString()) Then
                        Return EnsureTrailingSlash(urlToken.ToString())
                    End If
                End If
            Catch
            End Try
            Return "http://localhost:5000/"
        End Function

        Private Shared Function EnsureTrailingSlash(url As String) As String
            Return If(url.EndsWith("/"), url, url & "/")
        End Function
    End Class

End Namespace
