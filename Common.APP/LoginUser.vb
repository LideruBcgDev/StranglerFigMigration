Imports System
Imports System.Collections.Generic
Imports Newtonsoft.Json

Namespace Global.Common.APP

    ''' <summary>
    ''' 【全プロジェクト共通ログインユーザ情報 (Common.APP.LoginUser)】
    ''' WinFormsClient / LegacyMvc / LegacyMvc.PRM / LegacyMvc.MDL / ModernBff / ModernBff.MDL の
    ''' どこからでも共通で参照・利用できるログインユーザクラスです。
    ''' </summary>
    <Serializable>
    Public Class LoginUser
        Public Const CookieName As String = "StranglerAuthToken"
        Public Const HeaderName As String = "X-Strangler-AuthToken"
        Public Const RedisKeyPrefix As String = "StranglerSession:LoginUser:"

        <JsonProperty("sessionToken")>
        Public Property SessionToken As String = String.Empty

        <JsonProperty("userId")>
        Public Property UserId As String = String.Empty

        <JsonProperty("userName")>
        Public Property UserName As String = String.Empty

        <JsonProperty("organizationCode")>
        Public Property OrganizationCode As String = String.Empty

        <JsonProperty("organizationName")>
        Public Property OrganizationName As String = String.Empty

        <JsonProperty("roles")>
        Public Property Roles As New List(Of String)()

        <JsonProperty("authenticationMethod")>
        Public Property AuthenticationMethod As String = "Password (USR_TBL)"

        <JsonProperty("identityProvider")>
        Public Property IdentityProvider As String = "LocalDB (USR_TBL)"

        <JsonProperty("authenticatedAtUtc")>
        Public Property AuthenticatedAtUtc As DateTime = DateTime.UtcNow

        <JsonProperty("isAuthenticated")>
        Public Property IsAuthenticated As Boolean = True

        ''' <summary>
        ''' どのように LoginUser が設定されたかの証跡
        ''' 例: "ViewController: IIdentity" / "ClientController: WinForms IParam.LoginUser" / "ModernBff: Redis"
        ''' </summary>
        <JsonProperty("sourceChannel")>
        Public Property SourceChannel As String = String.Empty

        Public Function ToSummaryString() As String
            Dim rolesText As String = If(Roles IsNot Nothing AndAlso Roles.Count > 0, String.Join(", ", Roles), "None")
            Return $"{UserName} ({UserId}) | 組織: [{OrganizationCode}] {OrganizationName} | 権限: {rolesText}"
        End Function
    End Class

End Namespace
