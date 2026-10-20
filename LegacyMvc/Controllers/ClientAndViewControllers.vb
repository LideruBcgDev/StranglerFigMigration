Imports System
Imports System.Collections.Generic
Imports System.Configuration
Imports System.IO
Imports System.Security.Principal
Imports System.Web.Mvc
Imports Common.APP
Imports LegacyMvc.MDL.Commands
Imports LegacyMvc.Models
Imports LegacyMvc.PRM
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Namespace Controllers

    Public Class ClientCommandRequestDto
        Public Property IParam As ItemSaveIParam
        Public Property ItmCd As String
        Public Property ItmNm As String
        Public Property Category As String
        Public Property UnitPrice As Decimal
        Public Property StockQty As Integer
    End Class

    ''' <summary>
    ''' 【レガシー側 ClientController (RoutePrefix = "Client") - VB.NET】
    ''' Windows Forms クライアントからアクセスされる専用コントローラー (/Client/...)。
    ''' 1) コマンド名指定ディスパッチ (/Client/execute : "ITM.APP", "ITM.SCH", "ITM.SAV" 等)
    ''' 2) REST風エンドポイント (/Client/items, /Client/customer/{id})
    ''' の双方に対応し、返却された OParam は WinFormsClient 側で LegacyMvc.PRM の型へ DirectCast 可能です。
    ''' </summary>
    <RoutePrefix("Client")>
    Public Class ClientController
        Inherits Controller

        Private Sub PopulateClientIParamAndSession(iParam As IParamBase, Optional clientProvidedLoginUser As LoginUser = Nothing)
            If Session IsNot Nothing Then
                iParam.SessionId = Session.SessionID
                Session("LastClientControllerAccessUtc") = DateTime.UtcNow.ToString("O")
                For Each key As String In Session.Keys
                    If Not String.IsNullOrWhiteSpace(key) AndAlso Session(key) IsNot Nothing Then
                        iParam.SessionData(key) = Convert.ToString(Session(key))
                    End If
                Next
            End If

            If clientProvidedLoginUser IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(clientProvidedLoginUser.UserId) Then
                clientProvidedLoginUser.SourceChannel = "ClientController: WindowsForms RequestBody (command.IParam.LoginUser)"
                iParam.LoginUser = clientProvidedLoginUser
                Return
            End If

            Dim headerJson As String = Request.Headers("X-Client-IParam-LoginUser")
            If Not String.IsNullOrWhiteSpace(headerJson) Then
                Try
                    Dim fromHeader As LoginUser = JsonConvert.DeserializeObject(Of LoginUser)(Uri.UnescapeDataString(headerJson))
                    If fromHeader IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(fromHeader.UserId) Then
                        fromHeader.SourceChannel = "ClientController: WindowsForms Header (command.IParam.LoginUser)"
                        iParam.LoginUser = fromHeader
                        Return
                    End If
                Catch
                End Try
            End If

            Dim queryUserId As String = Request.QueryString("loginUserId")
            If Not String.IsNullOrWhiteSpace(queryUserId) Then
                iParam.LoginUser = New LoginUser With {
                    .UserId = queryUserId,
                    .UserName = If(Request.QueryString("loginUserName"), queryUserId),
                    .OrganizationCode = "ORG-WINFORMS",
                    .OrganizationName = "WinForms クライアント指定",
                    .Roles = New List(Of String) From {If(Request.QueryString("role"), "Engineer")},
                    .AuthenticationMethod = "WinForms-IParam.LoginUser",
                    .IsAuthenticated = True,
                    .SourceChannel = "ClientController: WindowsForms Query (command.IParam.LoginUser)"
                }
                Return
            End If

            Dim lookupStatus As String = String.Empty
            Dim redisUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, lookupStatus)
            If redisUser IsNot Nothing Then
                redisUser.SourceChannel = $"ClientController: WindowsForms Token->Redis ({lookupStatus})"
                iParam.LoginUser = redisUser
            Else
                iParam.LoginUser = New LoginUser With {
                    .UserId = "yamada",
                    .UserName = "山田 太郎",
                    .OrganizationCode = "SALES-01",
                    .OrganizationName = "第一営業本部",
                    .Roles = New List(Of String) From {"SalesManager", "Approver"},
                    .AuthenticationMethod = "WinForms-Default-IParam.LoginUser",
                    .IsAuthenticated = True,
                    .SourceChannel = "ClientController: WindowsForms Default (command.IParam.LoginUser)"
                }
            End If
        End Sub

        ''' <summary>
        ''' POST / GET: /Client/execute
        ''' Config (&lt;command name="ITM.APP" command="LegacyMvc.MDL.CommandsLegacyApprovalStore,LegacyMvc.MDL" db="1" /&gt;)
        ''' に基づき、コマンド名から ICommand インスタンスを動的生成して Validate() -&gt; Execute() を実行します。
        ''' 返却される OParam は LegacyMvc.PRM の型情報 (oParamType) 付きで返り、WinForms 側で DirectCast 可能です。
        ''' </summary>
        <HttpPost>
        <HttpGet>
        <Route("execute")>
        Public Function ExecuteCommandByName(Optional commandName As String = Nothing) As ActionResult
            Dim iParamJson As JToken = Nothing
            Dim bodyLoginUser As LoginUser = Nothing

            If String.Equals(Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) Then
                Dim body As String
                Using reader As New StreamReader(Request.InputStream)
                    body = reader.ReadToEnd()
                End Using

                If Not String.IsNullOrWhiteSpace(body) Then
                    Dim root As JObject = JObject.Parse(body)
                    If String.IsNullOrWhiteSpace(commandName) Then
                        commandName = Convert.ToString(If(root("commandName"), root("CommandName")))
                    End If
                    iParamJson = If(root("iParam"), root("IParam"))
                    If iParamJson IsNot Nothing AndAlso iParamJson.Type = JTokenType.Object Then
                        Dim luToken As JToken = If(iParamJson("loginUser"), iParamJson("LoginUser"))
                        If luToken IsNot Nothing Then
                            bodyLoginUser = luToken.ToObject(Of LoginUser)()
                        End If
                    End If
                End If
            End If

            If String.IsNullOrWhiteSpace(commandName) Then
                commandName = If(Request.QueryString("commandName"), "ITM.APP")
            End If

            Dim resolvedEntry As CommandConfigEntry = Nothing
            Dim cmd As ICommand = CommandDispatcher.CreateCommand(commandName, iParamJson, resolvedEntry)

            PopulateClientIParamAndSession(cmd.IParam, bodyLoginUser)

            Try
                cmd.Validate()
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Content(JsonConvert.SerializeObject(New With {
                    .controller = "LegacyMvc.Controllers.ClientController (VB.NET)",
                    .commandName = resolvedEntry.Name,
                    .configDefinition = resolvedEntry.CommandTypeString,
                    .db = resolvedEntry.Db,
                    .oParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                    .iParam = cmd.IParam,
                    .oParam = cmd.OParam,
                    .error = ex.Message
                }), "application/json")
            End Try

            Dim responseJson As String = JsonConvert.SerializeObject(New With {
                .controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .commandName = resolvedEntry.Name,
                .configDefinition = resolvedEntry.CommandTypeString,
                .registrationSource = resolvedEntry.RegistrationSource,
                .db = resolvedEntry.Db,
                .responseType = cmd.ResponseType,
                .iParamType = cmd.IParam.GetType().AssemblyQualifiedName,
                .oParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                .iParam = cmd.IParam,
                .oParam = cmd.OParam
            })

            Return Content(responseJson, "application/json")
        End Function

        ' GET: /Client/items
        <HttpGet>
        <Route("items")>
        Public Function GetItems(Optional itmCd As String = Nothing) As ActionResult
            Dim configuredProvider As String = If(ConfigurationManager.AppSettings("DatabaseProvider"), "SqlServer")
            Dim connSettings = ConfigurationManager.ConnectionStrings(configuredProvider)
            Dim connStr As String = If(connSettings IsNot Nothing, connSettings.ConnectionString, String.Empty)

            Dim cmd As New ItemSearchCommand()
            cmd.TypedIParam.ConfiguredProvider = configuredProvider
            cmd.TypedIParam.ConnectionString = connStr
            cmd.TypedIParam.Keyword = itmCd
            cmd.TypedIParam.CommandId = "ITM.SCH"

            PopulateClientIParamAndSession(cmd.IParam)

            Try
                cmd.Validate()
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                    .CommandName = "ITM.SCH",
                    .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                }, JsonRequestBehavior.AllowGet)
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .AccessedFrom = "Windows Forms Client",
                .CommandName = "ITM.SCH",
                .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                .ResponseType = cmd.ResponseType,
                .IParam = cmd.IParam,
                .OParam = cmd.OParam,
                .DatabaseProviderStatus = cmd.TypedOParam.ProviderStatus,
                .ItemCount = cmd.TypedOParam.Items.Count,
                .Items = cmd.TypedOParam.Items,
                .TimestampUtc = DateTime.UtcNow.ToString("O")
            }, JsonRequestBehavior.AllowGet)
        End Function

        ' POST: /Client/items
        <HttpPost>
        <Route("items")>
        Public Function RegisterItem() As ActionResult
            Dim body As String
            Using reader As New StreamReader(Request.InputStream)
                body = reader.ReadToEnd()
            End Using

            Dim reqDto As ClientCommandRequestDto = JsonConvert.DeserializeObject(Of ClientCommandRequestDto)(body)
            Dim configuredProvider As String = If(ConfigurationManager.AppSettings("DatabaseProvider"), "SqlServer")
            Dim connSettings = ConfigurationManager.ConnectionStrings(configuredProvider)
            Dim connStr As String = If(connSettings IsNot Nothing, connSettings.ConnectionString, String.Empty)

            Dim cmd As New ItemSaveCommand()
            cmd.TypedIParam.ConfiguredProvider = configuredProvider
            cmd.TypedIParam.ConnectionString = connStr
            cmd.TypedIParam.ItmCd = If(reqDto?.IParam?.ItmCd, reqDto?.ItmCd)
            cmd.TypedIParam.ItmNm = If(reqDto?.IParam?.ItmNm, reqDto?.ItmNm)
            cmd.TypedIParam.Category = If(reqDto?.IParam?.Category, reqDto?.Category)
            cmd.TypedIParam.UnitPrice = If(reqDto?.IParam IsNot Nothing AndAlso reqDto.IParam.UnitPrice > 0D, reqDto.IParam.UnitPrice, If(reqDto IsNot Nothing, reqDto.UnitPrice, 0D))
            cmd.TypedIParam.StockQty = If(reqDto?.IParam IsNot Nothing AndAlso reqDto.IParam.StockQty > 0, reqDto.IParam.StockQty, If(reqDto IsNot Nothing, reqDto.StockQty, 0))
            cmd.TypedIParam.CommandId = "ITM.SAV"

            Dim clientLoginUser As LoginUser = reqDto?.IParam?.LoginUser
            PopulateClientIParamAndSession(cmd.IParam, clientLoginUser)

            Try
                cmd.Validate()
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                    .CommandName = "ITM.SAV",
                    .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                })
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .CommandName = "ITM.SAV",
                .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                .ResponseType = cmd.ResponseType,
                .IParam = cmd.IParam,
                .OParam = cmd.OParam,
                .Message = $"[Legacy ClientController -> ICommand] 品目 '{cmd.TypedOParam.SavedItem.ItmCd}' を ITM_TBL に登録・更新しました。",
                .SavedItem = cmd.TypedOParam.SavedItem
            })
        End Function

        ' GET: /Client/customer/{id}
        <HttpGet>
        <Route("customer/{id:int}")>
        Public Function GetCustomer(Optional id As Integer = 1) As ActionResult
            Dim cmd As New CustomerQueryCommand()
            cmd.TypedIParam.CustomerId = id
            cmd.TypedIParam.CommandId = "CUS.SCH"

            PopulateClientIParamAndSession(cmd.IParam)

            Try
                cmd.Validate()
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                    .CommandName = "CUS.SCH",
                    .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                }, JsonRequestBehavior.AllowGet)
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .CommandName = "CUS.SCH",
                .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                .ResponseType = cmd.ResponseType,
                .IParam = cmd.IParam,
                .OParam = cmd.OParam,
                .CustomerId = cmd.TypedOParam.CustomerId,
                .CustomerName = cmd.TypedOParam.CustomerName,
                .TimestampUtc = DateTime.UtcNow.ToString("O")
            }, JsonRequestBehavior.AllowGet)
        End Function

    End Class

    ''' <summary>
    ''' 【レガシー側 ViewController (RoutePrefix = "View") - VB.NET】
    ''' Web ブラウザからアクセスされる専用コントローラー (/View/...)。
    ''' </summary>
    <RoutePrefix("View")>
    Public Class ViewController
        Inherits Controller

        Private Sub PopulateViewIParamFromIIdentity(iParam As IParamBase)
            If Session IsNot Nothing Then
                iParam.SessionId = Session.SessionID
                Session("LastViewControllerAccessUtc") = DateTime.UtcNow.ToString("O")
                For Each key As String In Session.Keys
                    If Not String.IsNullOrWhiteSpace(key) AndAlso Session(key) IsNot Nothing Then
                        iParam.SessionData(key) = Convert.ToString(Session(key))
                    End If
                Next
            End If

            Dim lookupStatus As String = String.Empty
            Dim redisUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, lookupStatus)
            If redisUser IsNot Nothing AndAlso (Me.User Is Nothing OrElse Me.User.Identity Is Nothing OrElse Not Me.User.Identity.IsAuthenticated) Then
                Dim genericIdentity As New GenericIdentity(redisUser.UserId, If(redisUser.AuthenticationMethod, "FormsOrOidc"))
                Dim rolesArray As String() = If(redisUser.Roles IsNot Nothing, redisUser.Roles.ToArray(), New String() {})
                Me.HttpContext.User = New GenericPrincipal(genericIdentity, rolesArray)
            End If

            Dim identity As IIdentity = If(Me.User IsNot Nothing, Me.User.Identity, Nothing)
            If identity IsNot Nothing AndAlso identity.IsAuthenticated AndAlso Not String.IsNullOrWhiteSpace(identity.Name) Then
                iParam.LoginUser = New LoginUser With {
                    .UserId = identity.Name,
                    .UserName = If(redisUser IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(redisUser.UserName), redisUser.UserName, identity.Name),
                    .OrganizationCode = If(redisUser IsNot Nothing, redisUser.OrganizationCode, "ENG-01"),
                    .OrganizationName = If(redisUser IsNot Nothing, redisUser.OrganizationName, "技術開発部"),
                    .Roles = If(redisUser IsNot Nothing AndAlso redisUser.Roles IsNot Nothing, redisUser.Roles, New List(Of String) From {"Engineer"}),
                    .AuthenticationMethod = identity.AuthenticationType,
                    .IsAuthenticated = identity.IsAuthenticated,
                    .SourceChannel = $"ViewController: IIdentity (Name={identity.Name}, AuthType={identity.AuthenticationType})"
                }
            Else
                Dim fallbackIdentity As IIdentity = New GenericIdentity("suzuki", "LegacyWebFormsIdentity")
                iParam.LoginUser = New LoginUser With {
                    .UserId = fallbackIdentity.Name,
                    .UserName = "鈴木 花子",
                    .OrganizationCode = "ENG-01",
                    .OrganizationName = "技術開発部",
                    .Roles = New List(Of String) From {"Engineer"},
                    .AuthenticationMethod = fallbackIdentity.AuthenticationType,
                    .IsAuthenticated = fallbackIdentity.IsAuthenticated,
                    .SourceChannel = $"ViewController: IIdentity Fallback ({fallbackIdentity.GetType().Name}, Name={fallbackIdentity.Name})"
                }
            End If
        End Sub

        ' POST / GET: /View/execute?commandName=DRW.SCH
        <HttpPost>
        <HttpGet>
        <Route("execute")>
        Public Function ExecuteCommandByName(Optional commandName As String = "DRW.SCH") As ActionResult
            Dim iParamJson As JToken = Nothing
            If String.Equals(Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) Then
                Dim body As String
                Using reader As New StreamReader(Request.InputStream)
                    body = reader.ReadToEnd()
                End Using
                If Not String.IsNullOrWhiteSpace(body) Then
                    Dim root As JObject = JObject.Parse(body)
                    commandName = Convert.ToString(If(root("commandName"), If(root("CommandName"), commandName)))
                    iParamJson = If(root("iParam"), root("IParam"))
                End If
            End If

            Dim resolvedEntry As CommandConfigEntry = Nothing
            Dim cmd As ICommand = CommandDispatcher.CreateCommand(commandName, iParamJson, resolvedEntry)
            PopulateViewIParamFromIIdentity(cmd.IParam)
            cmd.Validate()
            cmd.Execute()

            Dim responseJson As String = JsonConvert.SerializeObject(New With {
                .controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                .commandName = resolvedEntry.Name,
                .configDefinition = resolvedEntry.CommandTypeString,
                .db = resolvedEntry.Db,
                .oParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                .iParam = cmd.IParam,
                .oParam = cmd.OParam
            })

            Return Content(responseJson, "application/json")
        End Function

        ' GET: /View/drawings
        <HttpGet>
        <Route("drawings")>
        Public Function GetDrawings(Optional keyword As String = Nothing) As ActionResult
            Dim cmd As New DrawingSearchCommand()
            cmd.TypedIParam.Keyword = keyword
            cmd.TypedIParam.CommandId = "DRW.SCH"

            PopulateViewIParamFromIIdentity(cmd.IParam)

            Try
                cmd.Validate()
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                    .CommandName = "DRW.SCH",
                    .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                }, JsonRequestBehavior.AllowGet)
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                .AccessedFrom = "Web Browser",
                .CommandName = "DRW.SCH",
                .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                .ResponseType = cmd.ResponseType,
                .IParam = cmd.IParam,
                .OParam = cmd.OParam,
                .DrawingCount = cmd.TypedOParam.Drawings.Count,
                .Drawings = cmd.TypedOParam.Drawings,
                .TimestampUtc = DateTime.UtcNow.ToString("O")
            }, JsonRequestBehavior.AllowGet)
        End Function

        ' POST: /View/drawings
        <HttpPost>
        <Route("drawings")>
        Public Function RegisterDrawing() As ActionResult
            Dim body As String
            Using reader As New StreamReader(Request.InputStream)
                body = reader.ReadToEnd()
            End Using

            Dim model As DrawingPostModel = JsonConvert.DeserializeObject(Of DrawingPostModel)(body)

            Dim cmd As New DrawingSaveCommand()
            cmd.TypedIParam.DrawingNo = model?.DrawingNo
            cmd.TypedIParam.Title = model?.Title
            cmd.TypedIParam.ItmCd = model?.ItmCd
            cmd.TypedIParam.BomNo = model?.BomNo
            cmd.TypedIParam.Revision = model?.Revision
            cmd.TypedIParam.CommandId = "DRW.SAV"

            PopulateViewIParamFromIIdentity(cmd.IParam)

            Try
                cmd.Validate()
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                    .CommandName = "DRW.SAV",
                    .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                })
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                .CommandName = "DRW.SAV",
                .OParamType = cmd.OParam.GetType().AssemblyQualifiedName,
                .ResponseType = cmd.ResponseType,
                .IParam = cmd.IParam,
                .OParam = cmd.OParam,
                .Message = $"[Legacy ViewController -> ICommand] 図面 '{cmd.TypedOParam.SavedDrawing.DrawingNo}' を DRW_TBL に登録・更新しました。",
                .SavedDrawing = cmd.TypedOParam.SavedDrawing
            })
        End Function

    End Class

End Namespace
