Imports System.IO
Imports System.IO.Compression
Imports System.Text.RegularExpressions

''' <summary>
''' 一个 Mod 文件的元数据。
''' </summary>
Public Class McModMetadata
    ''' <summary>显示名称。</summary>
    Public Property Name As String = ""
    ''' <summary>Mod 的标识符（如 sodium_extra）。</summary>
    Public Property ModId As String = ""
    ''' <summary>作者。</summary>
    Public Property Authors As String = ""
    ''' <summary>版本号。</summary>
    Public Property Version As String = ""
    ''' <summary>Mod 自带的简短描述。</summary>
    Public Property Description As String = ""
    ''' <summary>主页地址。</summary>
    Public Property Homepage As String = ""
    ''' <summary>图标在 jar 内的路径。</summary>
    Public Property IconEntry As String = ""
End Class

''' <summary>
''' 读取本地 Mod 文件的元数据，并从 Modrinth 获取完整介绍。
''' </summary>
Public Module ModModMetadata

#Region "读取 jar 元数据"

    ''' <summary>
    ''' 读取指定 Mod 文件的元数据。支持 Fabric、Quilt、Forge、NeoForge 与旧版 mcmod.info。
    ''' 读取失败时返回一个只带文件名的兜底对象。
    ''' </summary>
    Public Function ReadFromJar(JarPath As String) As McModMetadata
        Dim Info As New McModMetadata With {.Name = PathUtils.GetFileNameWithoutExtension(JarPath)}
        Try
            If Not FileUtils.Exists(JarPath) Then Return Info
            Using Zip As ZipArchive = ZipFile.OpenRead(JarPath)
                'Fabric
                If FillFromJson(Zip, "fabric.mod.json", Info, "id", "name", "description", "version", "icon") Then Return Info
                'Quilt
                If FillFromJson(Zip, "quilt.mod.json", Info, "quilt_loader.id", "quilt_loader.metadata.name", "quilt_loader.metadata.description", "quilt_loader.version", "quilt_loader.metadata.icon") Then Return Info
                'Forge / NeoForge
                For Each TomlName As String In {"META-INF/neoforge.mods.toml", "META-INF/mods.toml", "mods.toml"}
                    Dim Entry As ZipArchiveEntry = Zip.GetEntry(TomlName)
                    If Entry IsNot Nothing Then
                        FillFromToml(ReadEntryText(Entry), Info)
                        Return Info
                    End If
                Next
                '旧版 mcmod.info
                Dim OldEntry As ZipArchiveEntry = Zip.GetEntry("mcmod.info")
                If OldEntry IsNot Nothing Then FillFromJsonText(ReadEntryText(OldEntry), Info, "modid", "name", "description", "version", "logoFile")
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"读取 Mod 元数据失败：{JarPath}")
        End Try
        Return Info
    End Function

    Private Function ReadEntryText(Entry As ZipArchiveEntry) As String
        Using Reader As New IO.StreamReader(Entry.Open(), Text.Encoding.UTF8)
            Return Reader.ReadToEnd()
        End Using
    End Function

    ''' <summary>从 jar 内某个 JSON 条目读取字段。</summary>
    Private Function FillFromJson(Zip As ZipArchive, EntryName As String, Info As McModMetadata,
                                  IdKey As String, NameKey As String, DescKey As String, VerKey As String, IconKey As String) As Boolean
        Dim Entry As ZipArchiveEntry = Zip.GetEntry(EntryName)
        If Entry Is Nothing Then Return False
        FillFromJsonText(ReadEntryText(Entry), Info, IdKey, NameKey, DescKey, VerKey, IconKey)
        Return True
    End Function

    Private Sub FillFromJsonText(Text As String, Info As McModMetadata,
                                 IdKey As String, NameKey As String, DescKey As String, VerKey As String, IconKey As String)
        Try
            Dim Json As JObject = Text.DeserializeJson()
            '旧版 mcmod.info 是数组
            If Json.Type = JTokenType.Array AndAlso Json.Count > 0 Then Json = CType(Json(0), JObject)
            Info.ModId = JsonPathString(Json, IdKey, Info.ModId)
            Info.Name = JsonPathString(Json, NameKey, Info.Name)
            Info.Description = JsonPathString(Json, DescKey, Info.Description)
            Info.Version = JsonPathString(Json, VerKey, Info.Version)
            Info.IconEntry = JsonPathString(Json, IconKey, Info.IconEntry)
        Catch ex As Exception
            Logger.Warn(ex, "解析 Mod 元数据 JSON 失败")
        End Try
    End Sub

    ''' <summary>按可能带点的路径取值，取不到则返回原值。</summary>
    Private Function JsonPathString(Json As JObject, Path As String, Fallback As String) As String
        Try
            Dim Node As JToken = Json
            For Each Part As String In Path.Split("."c)
                If Node Is Nothing Then Return Fallback
                Node = Node(Part)
            Next
            If Node Is Nothing Then Return Fallback
            Return If(Node.ToString.Trim = "", Fallback, Node.ToString.Trim)
        Catch
            Return Fallback
        End Try
    End Function

    ''' <summary>从 mods.toml 里提取基础字段（只取第一个 [[mods]] 段）。</summary>
    Private Sub FillFromToml(Text As String, Info As McModMetadata)
        Info.ModId = TomlValue(Text, "modId", Info.ModId)
        Info.Name = TomlValue(Text, "displayName", Info.Name)
        Info.Version = TomlValue(Text, "version", Info.Version)
        Info.Authors = TomlValue(Text, "authors", Info.Authors)
        Info.Homepage = TomlValue(Text, "displayURL", Info.Homepage)
        Info.IconEntry = TomlValue(Text, "logoFile", Info.IconEntry)
        Dim Icon As String = TomlValue(Text, "iconFile", "")
        If Icon <> "" Then Info.IconEntry = Icon
        Info.Description = TomlValue(Text, "description", Info.Description)
    End Sub

    ''' <summary>取 TOML 里某个键的值，支持单行引号与三引号多行。</summary>
    Private Function TomlValue(Text As String, Key As String, Fallback As String) As String
        Try
            '三单引号多行
            Dim Multi1 = Regex.Match(Text, "(?m)^\s*" & Key & "\s*=\s*'{3}(.*?)'{3}", RegexOptions.Singleline)
            If Multi1.Success Then Return Multi1.Groups(1).Value.Trim
            '三双引号多行
            Dim Quote As String = ChrW(34)
            Dim TripleQuote As String = New String(ChrW(34), 3)
            Dim Multi2 = Regex.Match(Text, "(?m)^\s*" & Key & "\s*=\s*" & TripleQuote & "(.*?)" & TripleQuote, RegexOptions.Singleline)
            If Multi2.Success Then Return Multi2.Groups(1).Value.Trim
            '单行：值可能是双引号或单引号包裹
            Dim Quoted As String = "[" & Quote & "']"
            Dim NotQuoted As String = "[^" & Quote & "']"
            Dim LineMatch = Regex.Match(Text, "(?m)^\s*" & Key & "\s*=\s*" & Quoted & "(" & NotQuoted & "*)" & Quoted)
            If LineMatch.Success Then Return LineMatch.Groups(1).Value.Trim
        Catch ex As Exception
            Logger.Warn(ex, $"解析 TOML 字段失败：{Key}")
        End Try
        Return Fallback
    End Function

#End Region

#Region "Modrinth"

    ''' <summary>
    ''' 在 Modrinth 上搜索该 Mod，返回最匹配的项目搜索结果；失败或无结果返回 Nothing。
    ''' </summary>
    Public Function SearchModrinth(Query As String) As JObject
        If String.IsNullOrWhiteSpace(Query) Then Return Nothing
        Try
            Dim Url As String = "https://api.modrinth.com/v2/search?limit=5&facets=%5B%5B%22project_type%3Amod%22%5D%5D&query=" & Net.WebUtility.UrlEncode(Query)
            Dim Result As JObject = NetRequestByClient(Url, RequireJson:=True, MakeLog:=False).DeserializeJson()
            Dim Hits As JToken = Result("hits")
            If Hits Is Nothing OrElse Hits.Count = 0 Then Return Nothing
            Return CType(Hits(0), JObject)
        Catch ex As Exception
            Logger.Warn(ex, $"Modrinth 搜索失败：{Query}")
            Return Nothing
        End Try
    End Function

    ''' <summary>获取 Modrinth 项目详情。body 字段是完整的 Markdown 介绍。</summary>
    Public Function GetModrinthProject(Id As String) As JObject
        If String.IsNullOrWhiteSpace(Id) Then Return Nothing
        Try
            Return NetRequestByClient($"https://api.modrinth.com/v2/project/{Id}", RequireJson:=True, MakeLog:=False).DeserializeJson()
        Catch ex As Exception
            Logger.Warn(ex, $"获取 Modrinth 项目失败：{Id}")
            Return Nothing
        End Try
    End Function

#End Region

End Module
