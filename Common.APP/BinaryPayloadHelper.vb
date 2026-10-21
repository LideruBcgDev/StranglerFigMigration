Imports System
Imports System.IO
Imports System.Runtime.Serialization.Formatters.Binary
Imports Newtonsoft.Json

Namespace Global.Common.APP

    ''' <summary>
    ''' 【BinaryFormatter over JSON 互換エンベロープ】
    ''' 既存の BinaryFormatter 前提の IParam / OParam (Interface 型プロパティ含有) を
    ''' HTTP JSON 通信上で安全に運ぶためのラッパー DTO です。
    ''' </summary>
    <Serializable>
    Public Class BinaryCommandEnvelope
        <JsonProperty("commandName")>
        Public Property CommandName As String = String.Empty

        <JsonProperty("isBinaryFormatted")>
        Public Property IsBinaryFormatted As Boolean = True

        ''' <summary>BinaryFormatter でシリアライズされた byte[] の Base64 文字列</summary>
        <JsonProperty("payloadBase64")>
        Public Property PayloadBase64 As String = String.Empty

        ''' <summary>デバッグ・ログ用の型名情報</summary>
        <JsonProperty("targetType")>
        Public Property TargetTypeName As String = String.Empty

        <JsonProperty("loginUser")>
        Public Property LoginUser As LoginUser

        <JsonProperty("timestampUtc")>
        Public Property TimestampUtc As DateTime = DateTime.UtcNow
    End Class

    ''' <summary>
    ''' 【BinaryFormatter / JSON シリアライズ補助ユーティリティ】
    ''' 1. テスト・互換用: BinaryFormatter でシリアライズした byte[] を Base64 文字列にして JSON に埋め込む方式
    ''' 2. モダン推奨案: Newtonsoft.Json の TypeNameHandling.Auto による $type 付与で Interface 型を復元する方式
    ''' の双方を提供します。
    ''' </summary>
    Public NotInheritable Class BinaryPayloadHelper

        Private Sub New()
        End Sub

        ' ====================================================================================
        ' 1. テスト用: BinaryFormatter <-> Base64 相互変換
        ' ====================================================================================

        ''' <summary>
        ''' オブジェクトを BinaryFormatter でシリアライズし、Base64 文字列として返します。
        ''' クラスおよび関連する Interface 実装型に [Serializable] 属性が必要です。
        ''' </summary>
        Public Shared Function SerializeToBase64(obj As Object) As String
            If obj Is Nothing Then Return String.Empty
            Dim formatter As New BinaryFormatter()
            Using ms As New MemoryStream()
                formatter.Serialize(ms, obj)
                Return Convert.ToBase64String(ms.ToArray())
            End Using
        End Function

        ''' <summary>
        ''' Base64 文字列から BinaryFormatter で元のオブジェクトツリー (Interface 型プロパティ含む) を復元します。
        ''' </summary>
        Public Shared Function DeserializeFromBase64(base64 As String) As Object
            If String.IsNullOrWhiteSpace(base64) Then Return Nothing
            Dim bytes As Byte() = Convert.FromBase64String(base64.Trim())
            Dim formatter As New BinaryFormatter()
            Using ms As New MemoryStream(bytes)
                Return formatter.Deserialize(ms)
            End Using
        End Function

        ''' <summary>
        ''' 型安全なジェネリック復元
        ''' </summary>
        Public Shared Function DeserializeFromBase64(Of T As Class)(base64 As String) As T
            Dim obj = DeserializeFromBase64(base64)
            Return TryCast(obj, T)
        End Function

        ' ====================================================================================
        ' 2. モダン推奨: Newtonsoft.Json の TypeNameHandling.Auto による Interface 型復元
        ' ====================================================================================

        Private Shared ReadOnly _typeSafeSettings As New JsonSerializerSettings With {
            .TypeNameHandling = TypeNameHandling.Auto,
            .NullValueHandling = NullValueHandling.Ignore,
            .ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        }

        ''' <summary>
        ''' Newtonsoft.Json の TypeNameHandling.Auto を使用して JSON 化します。
        ''' Interface 型や Object 型のプロパティには自動的に "$type" メタデータが付与されます。
        ''' </summary>
        Public Shared Function SerializeToJsonWithType(obj As Object) As String
            If obj Is Nothing Then Return String.Empty
            Return JsonConvert.SerializeObject(obj, _typeSafeSettings)
        End Function

        ''' <summary>
        ''' "$type" メタデータ付きの JSON 文字列から、Interface 型プロパティの具象クラスを完全復元してデシリアライズします。
        ''' </summary>
        Public Shared Function DeserializeFromJsonWithType(Of T)(json As String) As T
            If String.IsNullOrWhiteSpace(json) Then Return Nothing
            Return JsonConvert.DeserializeObject(Of T)(json, _typeSafeSettings)
        End Function

    End Class

End Namespace
