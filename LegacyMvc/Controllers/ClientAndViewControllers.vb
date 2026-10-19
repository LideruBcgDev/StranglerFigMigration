Imports System
Imports System.Collections.Generic
Imports System.Configuration
Imports System.IO
Imports System.Security.Principal
Imports System.Web.Mvc
Imports LegacyMvc.MDL.Commands
Imports LegacyMvc.Models
Imports Newtonsoft.Json

Namespace Controllers

    ''' <summary>
    ''' Windows Forms から ClientController へ送信されるリクエスト DTO
    ''' Windows Forms 側で設定された IParam.LoginUser を保持します。
    ''' </summary>
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
    ''' Windows Forms より設定された command.IParam.LoginUser 情報を取得し、
    ''' command.Validate() でログインユーザがコマンド利用可能か判断した上で command.Execute() を実行します。
    ''' </summary>
    <RoutePrefix("Client")>
    Public Class ClientController
        Inherits Controller

        ''' <summary>
        ''' Windows Forms クライアントから送信された command.IParam.LoginUser 情報を取得・復元します。
        ''' 1) POST JSON ボディ内の IParam.LoginUser
        ''' 2) リクエストヘッダー X-Client-IParam-LoginUser (GET リクエスト時にも WinForms が IParam.LoginUser を渡せるヘッダー)
        ''' 3) クエリパラメータ loginUserId (検証用)
        ''' 4) 未指定時は Redis 共有セッションまたは既定 WinForms ユーザにフォールバック
        ''' </summary>
        Private Sub PopulateClientIParamAndSession(iParam As IParamBase, Optional clientProvidedLoginUser As CommandLoginUser = Nothing)
            ' 1. ASP.NET Session 情報を IParamBase に格納
            If Session IsNot Nothing Then
                iParam.SessionId = Session.SessionID
                Session("LastClientControllerAccessUtc") = DateTime.UtcNow.ToString("O")
                For Each key As String In Session.Keys
                    If Not String.IsNullOrWhiteSpace(key) AndAlso Session(key) IsNot Nothing Then
                        iParam.SessionData(key) = Convert.ToString(Session(key))
                    End If
                Next
            End If

            ' 2. Windows Forms で設定された command.IParam.LoginUser の取得
            If clientProvidedLoginUser IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(clientProvidedLoginUser.UserId) Then
                clientProvidedLoginUser.SourceChannel = "ClientController: WindowsForms RequestBody (command.IParam.LoginUser)"
                iParam.LoginUser = clientProvidedLoginUser
                Return
            End If

            Dim headerJson As String = Request.Headers("X-Client-IParam-LoginUser")
            If Not String.IsNullOrWhiteSpace(headerJson) Then
                Try
                    Dim fromHeader As CommandLoginUser = JsonConvert.DeserializeObject(Of CommandLoginUser)(Uri.UnescapeDataString(headerJson))
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
                iParam.LoginUser = New CommandLoginUser With {
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

            ' フォールバック: Redis 共有セッションまたはデフォルトユーザ
            Dim lookupStatus As String = String.Empty
            Dim redisUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, lookupStatus)
            If redisUser IsNot Nothing Then
                iParam.LoginUser = New CommandLoginUser With {
                    .UserId = redisUser.UserId,
                    .UserName = redisUser.UserName,
                    .OrganizationCode = redisUser.OrganizationCode,
                    .OrganizationName = redisUser.OrganizationName,
                    .Roles = If(redisUser.Roles, New List(Of String)()),
                    .AuthenticationMethod = redisUser.AuthenticationMethod,
                    .IsAuthenticated = True,
                    .SourceChannel = $"ClientController: WindowsForms Token->Redis ({lookupStatus})"
                }
            Else
                iParam.LoginUser = New CommandLoginUser With {
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

        ' GET: /Client/items
        <HttpGet>
        <Route("items")>
        Public Function GetItems(Optional itmCd As String = Nothing) As ActionResult
            Dim configuredProvider As String = If(ConfigurationManager.AppSettings("DatabaseProvider"), "SqlServer")
            Dim connSettings = ConfigurationManager.ConnectionStrings(configuredProvider)
            Dim connStr As String = If(connSettings IsNot Nothing, connSettings.ConnectionString, String.Empty)

            ' ICommand 実装クラス (ItemSearchCommand) を生成
            Dim cmd As New ItemSearchCommand()
            cmd.TypedIParam.ConfiguredProvider = configuredProvider
            cmd.TypedIParam.ConnectionString = connStr
            cmd.TypedIParam.Keyword = itmCd
            cmd.TypedIParam.CommandId = "CMD_CLIENT_ITEM_SEARCH"

            ' Windows Forms より設定された command.IParam.LoginUser と Session 情報を設定
            PopulateClientIParamAndSession(cmd.IParam)

            Try
                ' 1. Validate() で LoginUser がこの Command を利用可能か判定
                cmd.Validate()
                ' 2. Execute() で LegacyMvc.MDL の実処理を実行
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                    .CommandInterface = "LegacyMvc.MDL.Commands.ICommand",
                    .ResponseType = cmd.ResponseType,
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                }, JsonRequestBehavior.AllowGet)
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .AccessedFrom = "Windows Forms Client",
                .CommandInterface = "LegacyMvc.MDL.Commands.ICommand (ItemSearchCommand)",
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
            cmd.TypedIParam.CommandId = "CMD_CLIENT_ITEM_SAVE"

            ' Windows Forms より設定された command.IParam.LoginUser を取得
            Dim clientLoginUser As CommandLoginUser = reqDto?.IParam?.LoginUser
            PopulateClientIParamAndSession(cmd.IParam, clientLoginUser)

            Try
                cmd.Validate()
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                    .CommandInterface = "LegacyMvc.MDL.Commands.ICommand (ItemSaveCommand)",
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                })
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .CommandInterface = "LegacyMvc.MDL.Commands.ICommand (ItemSaveCommand)",
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
            cmd.TypedIParam.CommandId = "CMD_CLIENT_CUSTOMER_QUERY"

            PopulateClientIParamAndSession(cmd.IParam)

            Try
                cmd.Validate()
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                    .CommandInterface = "LegacyMvc.MDL.Commands.ICommand (CustomerQueryCommand)",
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                }, JsonRequestBehavior.AllowGet)
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ClientController (VB.NET / RoutePrefix: Client)",
                .CommandInterface = "LegacyMvc.MDL.Commands.ICommand (CustomerQueryCommand)",
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
    ''' HttpContext.User.Identity (IIdentity) よりユーザ情報 (LoginUser) を取得して command.IParam.LoginUser に設定し、
    ''' command.Validate() でコマンド利用可能か判断した上で command.Execute() を実行します。
    ''' </summary>
    <RoutePrefix("View")>
    Public Class ViewController
        Inherits Controller

        ''' <summary>
        ''' ViewController では IIdentity (Me.User.Identity) よりユーザ情報 (LoginUser) を取得し、
        ''' Session 情報とともに command.IParam に設定します。
        ''' </summary>
        Private Sub PopulateViewIParamFromIIdentity(iParam As IParamBase)
            ' 1. ASP.NET Session 情報を IParamBase に格納
            If Session IsNot Nothing Then
                iParam.SessionId = Session.SessionID
                Session("LastViewControllerAccessUtc") = DateTime.UtcNow.ToString("O")
                For Each key As String In Session.Keys
                    If Not String.IsNullOrWhiteSpace(key) AndAlso Session(key) IsNot Nothing Then
                        iParam.SessionData(key) = Convert.ToString(Session(key))
                    End If
                Next
            End If

            ' 2. もし Redis 共有セッションが存在し、かつ現在の HttpContext.User.Identity が未認証であれば
            '    GenericIdentity / GenericPrincipal として HttpContext.User (IIdentity) に反映する
            Dim lookupStatus As String = String.Empty
            Dim redisUser As SharedLoginUser = SharedLoginUser.ResolveFromRedis(Request, lookupStatus)
            If redisUser IsNot Nothing AndAlso (Me.User Is Nothing OrElse Me.User.Identity Is Nothing OrElse Not Me.User.Identity.IsAuthenticated) Then
                Dim genericIdentity As New GenericIdentity(redisUser.UserId, If(redisUser.AuthenticationMethod, "FormsOrOidc"))
                Dim rolesArray As String() = If(redisUser.Roles IsNot Nothing, redisUser.Roles.ToArray(), New String() {})
                Me.HttpContext.User = New GenericPrincipal(genericIdentity, rolesArray)
            End If

            ' 3. IIdentity (Me.User.Identity) から LoginUser を取得して command.IParam.LoginUser に設定
            Dim identity As IIdentity = If(Me.User IsNot Nothing, Me.User.Identity, Nothing)
            If identity IsNot Nothing AndAlso identity.IsAuthenticated AndAlso Not String.IsNullOrWhiteSpace(identity.Name) Then
                iParam.LoginUser = New CommandLoginUser With {
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
                ' ブラウザ未ログイン時の検証用既定 IIdentity (GenericIdentity)
                Dim fallbackIdentity As IIdentity = New GenericIdentity("suzuki", "LegacyWebFormsIdentity")
                iParam.LoginUser = New CommandLoginUser With {
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

        ' GET: /View/drawings
        <HttpGet>
        <Route("drawings")>
        Public Function GetDrawings(Optional keyword As String = Nothing) As ActionResult
            Dim cmd As New DrawingSearchCommand()
            cmd.TypedIParam.Keyword = keyword
            cmd.TypedIParam.CommandId = "CMD_VIEW_DRAWING_SEARCH"

            ' ViewController: IIdentity から LoginUser を取得して command.IParam に設定
            PopulateViewIParamFromIIdentity(cmd.IParam)

            Try
                ' 1. Validate() で LoginUser がコマンド利用可能か判断
                cmd.Validate()
                ' 2. Execute() で LegacyMvc.MDL の実処理を実行
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                    .CommandInterface = "LegacyMvc.MDL.Commands.ICommand (DrawingSearchCommand)",
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                }, JsonRequestBehavior.AllowGet)
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                .AccessedFrom = "Web Browser",
                .CommandInterface = "LegacyMvc.MDL.Commands.ICommand (DrawingSearchCommand)",
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
            cmd.TypedIParam.CommandId = "CMD_VIEW_DRAWING_SAVE"

            ' ViewController: IIdentity から LoginUser を取得して command.IParam に設定
            PopulateViewIParamFromIIdentity(cmd.IParam)

            Try
                cmd.Validate()
                cmd.Execute()
            Catch ex As UnauthorizedAccessException
                Response.StatusCode = 403
                Return Json(New With {
                    .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                    .CommandInterface = "LegacyMvc.MDL.Commands.ICommand (DrawingSaveCommand)",
                    .IParam = cmd.IParam,
                    .OParam = cmd.OParam,
                    .Error = ex.Message
                })
            End Try

            Return Json(New With {
                .Controller = "LegacyMvc.Controllers.ViewController (VB.NET / RoutePrefix: View)",
                .CommandInterface = "LegacyMvc.MDL.Commands.ICommand (DrawingSaveCommand)",
                .ResponseType = cmd.ResponseType,
                .IParam = cmd.IParam,
                .OParam = cmd.OParam,
                .Message = $"[Legacy ViewController -> ICommand] 図面 '{cmd.TypedOParam.SavedDrawing.DrawingNo}' を DRW_TBL に登録・更新しました。",
                .SavedDrawing = cmd.TypedOParam.SavedDrawing
            })
        End Function

    End Class

End Namespace
