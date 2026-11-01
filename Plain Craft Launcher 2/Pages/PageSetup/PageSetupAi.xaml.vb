Imports System.Windows.Documents
Imports System.Windows.Media

Public Class PageSetupAi

    Private Sub PageSetupAi_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '重复加载部分：每次进入页面都从设置里回填一次
        PanBack.ScrollToHome()
        AniControlEnabled += 1
        Try
            '把已保存的 API Key / 接口地址 / 权限 / 额外目录回填到控件上
            SettingService.RefreshSettings(Me)
            InitAiProviderCombo()
            InitAiHistory()
            InitLocalModelCombo()
            RefreshLocalAiStatus()
        Finally
            AniControlEnabled -= 1
        End Try

        '非重复加载部分
        Static IsLoaded As Boolean = False
        If IsLoaded Then Return
        IsLoaded = True
    End Sub

    ''' <summary>离开页面时把当前厂商的 Key 存起来。</summary>
    Private Sub PageSetupAi_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        SaveAiKeyFor(AiEditingProvider, TextAiKey.Text)
    End Sub

    ''' <summary>把字节数格式化成易读文本。</summary>
    Private Shared Function FormatLogSize(Size As Long) As String
        If Size >= 1024L * 1024 Then Return $"{Size / 1024.0 / 1024:0.##} MB"
        If Size >= 1024 Then Return $"{Size / 1024.0:0.#} KB"
        Return $"{Size} B"
    End Function

#Region "卡片：AI 崩溃分析"

    ''' <summary>当前选中的历史记录文件。</summary>
    Private SelectedAiHistory As String = Nothing

    ''' <summary>当前游戏目录；没选中版本时返回空字符串（分析日志时不算错误）。</summary>
    Private ReadOnly Property AiGameFolderOrEmpty As String
        Get
            If McInstanceSelected Is Nothing Then Return ""
            Return McInstanceSelected.PathIndie
        End Get
    End Property

    ''' <summary>AI 操作文件的根目录：当前选中的游戏实例目录。</summary>
    Private ReadOnly Property AiGameFolder As String
        Get
            If McInstanceSelected Is Nothing Then
                Throw New Exception("还没有选中游戏版本。请先在「启动」页选中一个版本，AI 需要知道游戏目录才能读写文件。")
            End If
            Return McInstanceSelected.PathIndie
        End Get
    End Property

    ''' <summary>分析最近的一份日志，并展示结果与修复建议。</summary>
    Private Sub BtnAiAnalyze_Click() Handles BtnAiAnalyze.Click
        If String.IsNullOrWhiteSpace(Settings.Get(Of String)("AiAnalyzeApiKey")) Then
            Hint("请先填写 API Key！", HintType.Red)
            Return
        End If
        Dim Folder As String = Paths.Base & "PCL\"
        If Not IO.Directory.Exists(Folder) Then
            Hint("还没有生成日志文件", HintType.Red)
            Return
        End If
        Dim Files As String() = IO.Directory.GetFiles(Folder, "Log*.txt")
        If Files.Length = 0 Then
            Hint("还没有生成日志文件", HintType.Red)
            Return
        End If
        AnalyzeLogFile(Files.OrderByDescending(Function(f) IO.File.GetLastWriteTime(f)).First())
    End Sub

    ''' <summary>把指定日志发给 AI 分析，完成后存历史并展示。</summary>
    Public Sub AnalyzeLogFile(LogPath As String)
        Dim Target As String = LogPath
        Dim Folder As String = AiGameFolderOrEmpty
        PanAiActions.Children.Clear()
        StartAiStream()
        Try
            PanAiStream.BringIntoView()
        Catch
        End Try
        If Folder = "" Then
            Hint("正在请求 AI 分析……（未选中游戏版本，AI 将无法读取版本内的文件）")
        Else
            Hint("正在请求 AI 分析，过程会实时显示在下方……")
        End If
        RunInThread(
        Sub()
            Try
                Dim Content As String = FileUtils.ReadAsString(Target)
                Dim Answer As String = AiAnalyzeWithTools(Content, Folder,
                                                          Sub(Msg) RunInUi(Sub() Hint(Msg)),
                                                          Sub(Piece) AiAppendStream(Piece))
                AiSaveHistory(IO.Path.GetFileName(Target), Content, Answer)
                RunInUi(
                Sub()
                    Try
                        SetAiStreamRunning(False)
                        InitAiHistory()
                        ShowAiResult(Answer, IO.Path.GetFileName(Target))
                    Catch ex As Exception
                        Hint($"展示结果失败：{ex.Message}", HintType.Red)
                    End Try
                End Sub)
            Catch ex As Exception
                RunInUi(Sub()
                                 SetAiStreamRunning(False)
                                 Hint($"AI 分析失败：{ex.Message}", HintType.Red)
                             End Sub)
            End Try
        End Sub)
    End Sub

    ''' <summary>已收到的完整流式文本。</summary>
    Private ReadOnly AiStreamFull As New Text.StringBuilder
    ''' <summary>上一次渲染的时间，用于节流。</summary>
    Private AiStreamLastRender As Date = Date.MinValue

    ''' <summary>收到一个分片：入缓冲，并立刻请求刷新（40ms 内最多刷一次）。</summary>
    Private Sub AiAppendStream(Piece As String)
        SyncLock AiStreamFull
            AiStreamFull.Append(Piece)
        End SyncLock
        If (Date.Now - AiStreamLastRender).TotalMilliseconds < 40 Then Return
        AiStreamLastRender = Date.Now
        RunInUi(Sub() PaintAiStream())
    End Sub

    ''' <summary>把当前缓冲内容渲染到显示区（JSON 建议块不显示）。</summary>
    Private Sub PaintAiStream()
        If PanAiStream Is Nothing Then Return
        Dim Full As String
        SyncLock AiStreamFull
            Full = AiStreamFull.ToString()
        End SyncLock
        Dim Cut As Integer = Full.IndexOf("```json", StringComparison.OrdinalIgnoreCase)
        Dim Visible As String = If(Cut >= 0, Full.Substring(0, Cut), Full)
        PanAiStream.Visibility = Visibility.Visible
        RenderAiMarkdown(Visible)
        PanAiStream.ScrollToEnd()
    End Sub

    ''' <summary>开始一次新的流式显示（需在 UI 线程调用）。</summary>
    Private Sub StartAiStream()
        SyncLock AiStreamFull
            AiStreamFull.Clear()
        End SyncLock
        AiStreamLastRender = Date.MinValue
        If PanAiStream.Document IsNot Nothing Then PanAiStream.Document.Blocks.Clear()
        PanAiStream.Visibility = Visibility.Visible
    End Sub

    ''' <summary>结束流式显示：把最终内容完整刷一遍。</summary>
    Private Sub StopAiStream()
        AiStreamLastRender = Date.MinValue
        RunInUi(Sub() PaintAiStream())
    End Sub

    ''' <summary>兼容旧调用名。</summary>
    Private Sub SetAiStreamRunning(Running As Boolean)
        If Running Then
            StartAiStream()
        Else
            StopAiStream()
        End If
    End Sub

    ''' <summary>把 Markdown 文本渲染成富文本。</summary>
    Private Sub RenderAiMarkdown(Markdown As String)
        Dim Doc As New FlowDocument With {
            .FontFamily = New FontFamily("Microsoft YaHei UI"),
            .FontSize = 12.5,
            .PagePadding = New Thickness(0),
            .LineHeight = 19}
        Dim Lines As String() = Markdown.Replace(vbCrLf, vbLf).Split(ChrW(10))
        Dim InCode As Boolean = False
        Dim CodeBuf As New Text.StringBuilder
        For Each RawLine As String In Lines
            Dim Trimmed As String = RawLine.TrimStart()
            If Trimmed.StartsWith("```") Then
                If InCode Then
                    Doc.Blocks.Add(BuildAiCodeBlock(CodeBuf.ToString()))
                    CodeBuf.Clear()
                    InCode = False
                Else
                    InCode = True
                End If
                Continue For
            End If
            If InCode Then
                CodeBuf.AppendLine(RawLine)
                Continue For
            End If
            If RawLine.Trim() = "" Then Continue For
            If Trimmed.StartsWith("### ") Then
                Doc.Blocks.Add(BuildAiHeading(Trimmed.Substring(4), 13.5))
            ElseIf Trimmed.StartsWith("## ") Then
                Doc.Blocks.Add(BuildAiHeading(Trimmed.Substring(3), 15))
            ElseIf Trimmed.StartsWith("# ") Then
                Doc.Blocks.Add(BuildAiHeading(Trimmed.Substring(2), 17))
            ElseIf Trimmed.StartsWith("- ") OrElse Trimmed.StartsWith("* ") Then
                Dim Item As Paragraph = BuildAiParagraph(Trimmed.Substring(2))
                Item.Margin = New Thickness(12, 0, 0, 5)
                Item.Inlines.InsertBefore(Item.Inlines.FirstInline, New Run("· "))
                Doc.Blocks.Add(Item)
            Else
                Doc.Blocks.Add(BuildAiParagraph(RawLine))
            End If
        Next
        If InCode AndAlso CodeBuf.Length > 0 Then Doc.Blocks.Add(BuildAiCodeBlock(CodeBuf.ToString()))
        PanAiStream.Document = Doc
    End Sub

    Private Function BuildAiHeading(Text As String, Size As Double) As Paragraph
        Return New Paragraph(New Run(Text)) With {
            .FontSize = Size,
            .FontWeight = FontWeights.Bold,
            .Margin = New Thickness(0, 8, 0, 4)}
    End Function

    Private Function BuildAiCodeBlock(Code As String) As Paragraph
        Return New Paragraph(New Run(Code.TrimEnd())) With {
            .FontFamily = New FontFamily("Consolas"),
            .FontSize = 11.5,
            .Background = New SolidColorBrush(Color.FromRgb(238, 238, 238)),
            .Padding = New Thickness(8, 6, 8, 6),
            .Margin = New Thickness(0, 4, 0, 8)}
    End Function

    ''' <summary>构造段落，并处理行内的 **粗体** 与 `代码`。</summary>
    Private Function BuildAiParagraph(Text As String) As Paragraph
        Dim P As New Paragraph With {.Margin = New Thickness(0, 0, 0, 6)}
        Dim Buf As New Text.StringBuilder
        Dim i As Integer = 0
        Do While i < Text.Length
            If i + 1 < Text.Length AndAlso Text.Substring(i, 2) = "**" Then
                Dim ClosePos As Integer = Text.IndexOf("**", i + 2, StringComparison.Ordinal)
                If ClosePos > i + 2 Then
                    If Buf.Length > 0 Then
                        P.Inlines.Add(New Run(Buf.ToString()))
                        Buf.Clear()
                    End If
                    P.Inlines.Add(New Bold(New Run(Text.Substring(i + 2, ClosePos - i - 2))))
                    i = ClosePos + 2
                    Continue Do
                End If
            End If
            If Text(i) = "`"c Then
                Dim ClosePos As Integer = Text.IndexOf("`"c, i + 1)
                If ClosePos > i + 1 Then
                    If Buf.Length > 0 Then
                        P.Inlines.Add(New Run(Buf.ToString()))
                        Buf.Clear()
                    End If
                    P.Inlines.Add(New Run(Text.Substring(i + 1, ClosePos - i - 1)) With {
                        .FontFamily = New FontFamily("Consolas"),
                        .Background = New SolidColorBrush(Color.FromRgb(238, 238, 238))})
                    i = ClosePos + 1
                    Continue Do
                End If
            End If
            Buf.Append(Text(i))
            i += 1
        Loop
        If Buf.Length > 0 Then P.Inlines.Add(New Run(Buf.ToString()))
        Return P
    End Function

    ''' <summary>当前正在编辑的厂商 Id，用于切换前保存它的 Key。</summary>
    Private AiEditingProvider As String = ""

    ''' <summary>各厂商已保存的 API Key（JSON：厂商Id → Key）。</summary>
    Private Function GetAiKeyStore() As JObject
        Dim Raw As String = Settings.Get(Of String)("AiKeyStore")
        If String.IsNullOrWhiteSpace(Raw) Then Return New JObject
        Try
            Return Raw.DeserializeJson()
        Catch
            Return New JObject
        End Try
    End Function

    Private Sub SaveAiKeyFor(ProviderId As String, Key As String)
        If ProviderId = "" OrElse ProviderId = "__custom__" Then Return
        Dim Store As JObject = GetAiKeyStore()
        Store(ProviderId) = Key
        Settings.Set("AiKeyStore", Store.ToString())
    End Sub

    Private Function LoadAiKeyFor(ProviderId As String) As String
        If ProviderId = "" OrElse ProviderId = "__custom__" Then Return ""
        Dim Store As JObject = GetAiKeyStore()
        Return If(Store(ProviderId)?.ToString, "")
    End Function

    ''' <summary>填充服务商下拉列表，Filter 非空时按关键词过滤。</summary>
    Private Sub InitAiProviderCombo(Optional Filter As String = "")
        If ComboAiProvider Is Nothing Then Return
        Dim Keyword As String = If(Filter, "").Trim().ToLowerInvariant()
        AniControlEnabled += 1
        Try
            ComboAiProvider.Items.Clear()
            Dim CurrentId As String = Settings.Get(Of String)("AiSelectedProvider")
            Dim Target As Integer = -1
            For Each Line As String In AiProviders
                Dim Parts As String() = Line.Split("|"c)
                If Parts.Length < 4 Then Continue For
                Dim Id As String = Parts(0), NameText As String = Parts(1)
                If Keyword <> "" AndAlso Not (Id.ToLowerInvariant().Contains(Keyword) OrElse NameText.ToLowerInvariant().Contains(Keyword)) Then Continue For
                Dim Item As New MyComboBoxItem With {.Content = NameText, .Tag = Id}
                ComboAiProvider.Items.Add(Item)
                If Id = CurrentId Then Target = ComboAiProvider.Items.Count - 1
            Next
            Dim CustomItem As New MyComboBoxItem With {.Content = "自定义（手动填写地址与模型）", .Tag = "__custom__"}
            ComboAiProvider.Items.Add(CustomItem)
            If CurrentId = "__custom__" Then Target = ComboAiProvider.Items.Count - 1
            If Target >= 0 Then ComboAiProvider.SelectedIndex = Target
            AiEditingProvider = Settings.Get(Of String)("AiSelectedProvider")
            '首次迁移：已有 Key 但还没归档时，把它归给当前厂商
            If AiEditingProvider <> "" AndAlso AiEditingProvider <> "__custom__" AndAlso LoadAiKeyFor(AiEditingProvider) = "" Then
                Dim Existing As String = Settings.Get(Of String)("AiAnalyzeApiKey")
                If Existing <> "" Then SaveAiKeyFor(AiEditingProvider, Existing)
            End If
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    ''' <summary>搜索框内容变化时重新过滤服务商列表。</summary>
    Private Sub TextAiSearch_TextChanged() Handles TextAiSearch.TextChanged
        If AniControlEnabled > 0 Then Return
        InitAiProviderCombo(TextAiSearch.Text)
    End Sub

    ''' <summary>按服务商 Id 取接口地址与模型清单。</summary>
    Private Sub LoadProviderMeta(Id As String, ByRef Url As String, ByRef Models As List(Of String))
        Url = ""
        Models = New List(Of String)
        For Each Line As String In AiProviders
            Dim Parts As String() = Line.Split("|"c)
            If Parts.Length >= 3 AndAlso Parts(0) = Id Then
                Url = Parts(2).TrimEnd("/"c)
                Exit For
            End If
        Next
        For Each Line As String In AiModels
            Dim Parts As String() = Line.Split("|"c)
            If Parts.Length >= 2 AndAlso Parts(0) = Id Then Models.Add(Parts(1))
        Next
    End Sub

    ''' <summary>切换服务商：同步接口地址与模型下拉。</summary>
    Private Sub ComboAiProvider_SelectionChanged() Handles ComboAiProvider.SelectionChanged
        If AniControlEnabled > 0 Then Return
        Dim Item As MyComboBoxItem = TryCast(ComboAiProvider.SelectedItem, MyComboBoxItem)
        If Item Is Nothing OrElse Item.Tag Is Nothing Then Return
        Dim Id As String = Item.Tag.ToString
        Settings.Set("AiSelectedProvider", Id)
        '切换前先把当前厂商的 Key 存起来，再取出新厂商的 Key
        SaveAiKeyFor(AiEditingProvider, TextAiKey.Text)
        Dim NewKey As String = LoadAiKeyFor(Id)
        TextAiKey.Text = NewKey
        Settings.Set("AiAnalyzeApiKey", NewKey)
        AiEditingProvider = Id

        If Id = "__custom__" Then
            AniControlEnabled += 1
            Try
                ComboAiModel.Items.Clear()
                ComboAiModel.Items.Add(New MyComboBoxItem With {.Content = "（自定义，请在下方地址里填完整 URL）", .Tag = ""})
                ComboAiModel.SelectedIndex = 0
            Finally
                AniControlEnabled -= 1
            End Try
            TextAiUrl.Text = ""
            Settings.Set("AiAnalyzeApiUrl", "")
            Hint("已切换到自定义模式，请手动填写接口地址和模型名")
            Return
        End If

        Dim Url As String = "", ChooseModel As String = ""
        Dim Models As List(Of String) = Nothing
        LoadProviderMeta(Id, Url, Models)
        If Models.Count > 0 Then ChooseModel = Models(0)

        AniControlEnabled += 1
        Try
            ComboAiModel.Items.Clear()
            For Each M As String In Models
                ComboAiModel.Items.Add(New MyComboBoxItem With {.Content = M, .Tag = M})
            Next
            ComboAiModel.Items.Add(New MyComboBoxItem With {.Content = "自定义模型名…", .Tag = ""})
            If Models.Count > 0 Then ComboAiModel.SelectedIndex = 0 Else ComboAiModel.SelectedIndex = 0
        Finally
            AniControlEnabled -= 1
        End Try

        If Url <> "" Then
            TextAiUrl.Text = Url & "/chat/completions"
            Settings.Set("AiAnalyzeApiUrl", Url & "/chat/completions")
        End If
        If ChooseModel <> "" Then
            Settings.Set("AiAnalyzeModel", ChooseModel)
        End If
        Hint($"已切换到 {Item.Content}，请填写对应的 API Key")
    End Sub

    ''' <summary>选择模型时写入设置。</summary>
    Private Sub ComboAiModel_SelectionChanged() Handles ComboAiModel.SelectionChanged
        If AniControlEnabled > 0 Then Return
        Dim Item As MyComboBoxItem = TryCast(ComboAiModel.SelectedItem, MyComboBoxItem)
        If Item Is Nothing OrElse Item.Tag Is Nothing Then Return
        Dim ModelId As String = Item.Tag.ToString
        If ModelId <> "" Then Settings.Set("AiAnalyzeModel", ModelId)
    End Sub

    ''' <summary>把流式输出追加到显示区。</summary>
    Private Sub ShowAiResult(Answer As String, LogName As String)
        PanAiActions.Children.Clear()
        '把正文（已剥离 JSON 建议块）用 Markdown 渲染到上方的显示区，不再弹模态窗遮挡。
        RenderAiMarkdown(AiStripActions(Answer))
        PanAiStream.Visibility = Visibility.Visible
        Dim Actions As JArray = AiParseActions(Answer)
        If Actions Is Nothing OrElse Actions.Count = 0 Then Return
        Dim Lab As New TextBlock With {
            .Text = "可执行的修复建议（点击执行）：",
            .Margin = New Thickness(0, 12, 0, 6),
            .FontWeight = FontWeights.Bold}
        PanAiActions.Children.Add(Lab)
        For i = 0 To Actions.Count - 1
            Dim Act As JObject = CType(Actions(i), JObject)
            Dim ActCopy As JObject = Act '闭包捕获
            Dim TypeName As String = If(Act("type")?.ToString, "")
            Dim Rel As String = If(Act("path")?.ToString, "")
            Dim Desc As String = If(Act("desc")?.ToString, "")
            Dim Caption As String
            Select Case TypeName
                Case "delete_file"
                    Caption = $"删除 {Rel}"
                Case "write_file"
                    Caption = $"写入 {Rel}"
                Case "replace"
                    Caption = $"修改 {Rel}"
                Case "open_folder"
                    Caption = $"打开 {Rel}"
                Case "read_file"
                    Continue For
                Case Else
                    Caption = If(Desc <> "", "查看说明", "说明")
            End Select
            Dim Btn As New MyButton With {
                .Text = Caption,
                .Height = 32,
                .MinWidth = 160,
                .Margin = New Thickness(0, 0, 10, 8),
                .Padding = New Thickness(13, 0, 13, 0)}
            AddHandler Btn.Click,
                Sub(sender As Object, e As EventArgs)
                    RunAiAction(ActCopy)
                End Sub
            PanAiActions.Children.Add(Btn)
        Next
    End Sub

    ''' <summary>执行一条 AI 建议（含二次确认与越权拦截）。</summary>
    Private Sub RunAiAction(Act As JObject)
        Dim TypeName As String = If(Act("type")?.ToString, "")
        Dim Desc As String = If(Act("desc")?.ToString, "")
        Select Case TypeName
            Case "delete_file", "write_file", "replace", "open_folder"
                '下面统一处理
            Case Else
                MyMsgBox(If(Desc <> "", Desc, "这条建议需要你手动处理。"), "需要手动处理")
                Return
        End Select
        Dim Rel As String = If(Act("path")?.ToString, "")
        If Rel = "" Then
            Hint("这条建议没有给出路径", HintType.Red)
            Return
        End If
        Dim GameFolder As String
        Try
            GameFolder = AiGameFolder
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
            Return
        End Try
        Dim Full As String = IO.Path.Combine(GameFolder, Rel)
        Dim Warning As String
        Dim IsRisky As Boolean = (TypeName <> "open_folder")
        Select Case TypeName
            Case "delete_file"
                Warning = $"即将删除文件：{vbCrLf}{Full}{vbCrLf}{vbCrLf}这是 AI 根据日志给出的建议，请确认无误后再继续！"
            Case "write_file"
                Dim Preview As String = If(Act("content")?.ToString, "")
                If Preview.Length > 400 Then Preview = Preview.Substring(0, 400) & vbCrLf & "……（预览已截断）"
                Warning = $"即将覆盖写入文件：{vbCrLf}{Full}{vbCrLf}{vbCrLf}内容预览：{vbCrLf}{Preview}"
            Case "replace"
                Warning = $"即将修改文件：{vbCrLf}{Full}{vbCrLf}{vbCrLf}把这一段：{vbCrLf}{Act("old")}{vbCrLf}{vbCrLf}替换为：{vbCrLf}{Act("new")}"
            Case Else
                Warning = $"即将打开文件夹：{vbCrLf}{Full}"
        End Select
        Warning &= vbCrLf & vbCrLf & "原文件会自动备份到 AI 历史目录的 backup 文件夹。"
        If MyMsgBox(Warning, "执行 AI 建议", "执行", "取消", IsWarn:=IsRisky) <> 1 Then Return
        Try
            Hint(AiRunAction(Act, GameFolder), HintType.Green)
        Catch ex As Exception
            Hint(ex.Message, HintType.Red)
        End Try
    End Sub

    ''' <summary>扫描并显示分析历史。</summary>
    Public Sub InitAiHistory()
        If PanAiHistory Is Nothing Then Return
        PanAiHistory.Children.Clear()
        SelectedAiHistory = Nothing
        Dim Folder As String = AiHistoryFolder
        If Not IO.Directory.Exists(Folder) Then
            LabAiHistoryHint.Text = "还没有分析记录。点「分析最近日志」试一次吧。"
            Return
        End If
        Dim Files As List(Of String) = IO.Directory.GetFiles(Folder, "*.json").
            OrderByDescending(Function(f) IO.File.GetLastWriteTime(f)).ToList()
        If Files.Count = 0 Then
            LabAiHistoryHint.Text = "还没有分析记录。"
            Return
        End If
        LabAiHistoryHint.Text = $"共 {Files.Count} 条分析记录。选中后可查看或删除。"
        For Each HistoryFile As String In Files
            Dim Path As String = HistoryFile '闭包捕获
            Dim Title As String = IO.Path.GetFileNameWithoutExtension(Path)
            Dim TimeText As String = ""
            Dim Size As Long = 0
            Try
                Dim Json As JObject = FileUtils.ReadAsString(Path).DeserializeJson()
                TimeText = If(Json("time")?.ToString, "")
                Title = If(Json("log_file")?.ToString, Title)
            Catch
            End Try
            Try
                Size = New IO.FileInfo(Path).Length
            Catch
            End Try
            Dim Info As New IO.FileInfo(Path)
            Dim Item As New MyListItem With {
                .Type = MyListItem.CheckType.RadioBox,
                .Height = 44,
                .Title = If(TimeText <> "", TimeText, IO.Path.GetFileName(Path)),
                .Info = $"来源日志：{Title} · {FormatLogSize(Size)}"}
            AddHandler Item.Check,
                Sub(sender As Object, e As RouteEventArgs)
                    SelectedAiHistory = Path
                End Sub
            PanAiHistory.Children.Add(Item)
        Next
    End Sub

    Private Sub BtnAiHistoryRefresh_Click() Handles BtnAiHistoryRefresh.Click
        InitAiHistory()
        Hint("分析历史已刷新")
    End Sub

    Private Sub BtnAiHistoryView_Click() Handles BtnAiHistoryView.Click
        If SelectedAiHistory Is Nothing Then
            Hint("请先选中一条记录！", HintType.Red)
            Return
        End If
        Try
            Dim Json As JObject = FileUtils.ReadAsString(SelectedAiHistory).DeserializeJson()
            Dim Answer As String = Json("answer").ToString()
            ShowAiResult(Answer, Json("log_file").ToString())
        Catch ex As Exception
            Hint($"读取记录失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    Private Sub BtnAiHistoryDelete_Click() Handles BtnAiHistoryDelete.Click
        If SelectedAiHistory Is Nothing Then
            Hint("请先选中一条记录！", HintType.Red)
            Return
        End If
        If MyMsgBox($"确定要删除这条分析记录吗？{vbCrLf}{vbCrLf}{SelectedAiHistory}", "删除记录", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Try
            IO.File.Delete(SelectedAiHistory)
            Hint("记录已删除", HintType.Green)
            InitAiHistory()
        Catch ex As Exception
            Hint($"删除失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

#End Region


#Region "卡片：本地翻译模型"

    ''' <summary>是否有本地引擎操作正在执行（防止重复点击）。</summary>
    Private LocalAiBusy As Boolean = False

    ''' <summary>统一的本地引擎任务入口：自动防重复点击、自动刷新状态。</summary>
    Private Sub RunLocalAiTask(Name As String, Task As Action)
        If LocalAiBusy Then
            Hint("上一个操作还在进行中，请稍候……", HintType.Blue)
            Return
        End If
        LocalAiBusy = True
        RefreshLocalAiStatus()
        RunInThread(
        Sub()
            Dim Err As Exception = Nothing
            Try
                Task()
            Catch ex As Exception
                Err = ex
            End Try
            RunInUi(
            Sub()
                LocalAiBusy = False
                If Err Is Nothing Then
                    Hint($"{Name}完成", HintType.Green)
                Else
                    Hint($"{Name}失败：{Err.Message}", HintType.Red)
                End If
                RefreshLocalAiStatus()
            End Sub)
        End Sub)
    End Sub

    ''' <summary>刷新本地引擎状态与按钮可用性。</summary>
    Public Sub RefreshLocalAiStatus()
        If LabLocalAiStatus Is Nothing Then Return
        Dim Lines As New List(Of String)
        Lines.Add("运行环境：" & If(ModLocalTranslate.EngineReady, "已就绪", "未安装"))
        Dim Entry As String() = ModLocalTranslate.SelectedModelEntry()
        If ModLocalTranslate.ModelReady Then
            Lines.Add("翻译模型：已就绪（" & Entry(1) & "）")
        Else
            Lines.Add("翻译模型：未下载 · 已选 " & Entry(2) & "（" & Entry(3) & " MB）")
        End If
        Lines.Add("服务状态：" & ModLocalTranslate.StatusText())
        LabLocalAiStatus.Text = String.Join(vbCrLf, Lines)
        If BtnLocalAiInstall IsNot Nothing Then
            Dim CanClick As Boolean = Not LocalAiBusy
            BtnLocalAiInstall.IsEnabled = CanClick AndAlso Not ModLocalTranslate.EngineReady
            BtnLocalAiModel.IsEnabled = CanClick AndAlso ModLocalTranslate.EngineReady AndAlso Not ModLocalTranslate.ModelReady
            BtnLocalAiStart.IsEnabled = CanClick AndAlso ModLocalTranslate.EngineReady AndAlso ModLocalTranslate.ModelReady AndAlso Not ModLocalTranslate.ServerRunning
            BtnLocalAiStop.IsEnabled = CanClick AndAlso ModLocalTranslate.ServerRunning
            BtnLocalAiRemove.IsEnabled = CanClick
        End If
    End Sub

    Private Sub BtnLocalAiInstall_Click() Handles BtnLocalAiInstall.Click
        RunLocalAiTask("安装运行环境",
                       Sub() ModLocalTranslate.InstallEngine(Sub(P) RunInUi(Sub() ShowLocalAiProgress("运行环境", P, ModLocalTranslate.EngineSizeBytes))))
    End Sub

    Private Sub BtnLocalAiModel_Click() Handles BtnLocalAiModel.Click
        RunLocalAiTask("下载翻译模型",
                       Sub() ModLocalTranslate.InstallModel(Sub(P) RunInUi(Sub() ShowLocalAiProgress("翻译模型", P, ModLocalTranslate.ModelSizeBytes))))
    End Sub

    Private Sub BtnLocalAiStart_Click() Handles BtnLocalAiStart.Click
        RunLocalAiTask("启动本地服务",
                       Sub()
                           ModLocalTranslate.StartServer()
                           If Not ModLocalTranslate.WaitServerReady(60000) Then Throw New Exception("启动超时")
                       End Sub)
    End Sub

    Private Sub BtnLocalAiStop_Click() Handles BtnLocalAiStop.Click
        ModLocalTranslate.StopServer()
        Hint("本地翻译服务已停止")
        RefreshLocalAiStatus()
    End Sub

    Private Sub BtnLocalAiRemove_Click() Handles BtnLocalAiRemove.Click
        If MyMsgBox("确定要删除已下载的运行环境与翻译模型吗？" & vbCrLf & vbCrLf & ModLocalTranslate.LocalAiFolder,
                    "删除确认", "删除", "取消", IsWarn:=True) <> 1 Then Return
        ModLocalTranslate.UninstallAll()
        Hint("已删除", HintType.Green)
        RefreshLocalAiStatus()
    End Sub

#End Region


    ''' <summary>把下载进度写到状态文本上。</summary>
    Private Sub ShowLocalAiProgress(Name As String, Progress As Double, TotalBytes As Long)
        If LabLocalAiStatus Is Nothing Then Return
        Dim Pct As Double = Math.Max(0, Math.Min(1, Progress)) * 100
        If Pct <= 0.05 Then
            LabLocalAiStatus.Text = $"{Name}：正在连接并准备下载……（若长时间不动会自动切换到下一个镜像）"
        Else
            Dim Got As Long = CLng(Math.Max(0, Math.Min(1, Progress)) * TotalBytes)
            LabLocalAiStatus.Text = $"{Name}下载中：{FormatLocalSize(Got)} / {FormatLocalSize(TotalBytes)}（{Pct:0.0}%）"
        End If
    End Sub

    Private Shared Function FormatLocalSize(Size As Long) As String
        If Size >= 1024L * 1024 Then Return $"{Size / 1024.0 / 1024:0.0} MB"
        If Size >= 1024 Then Return $"{Size / 1024.0:0} KB"
        Return $"{Size} B"
    End Function


    ''' <summary>填充本地翻译模型下拉列表。</summary>
    Private Sub InitLocalModelCombo()
        If ComboLocalModel Is Nothing Then Return
        AniControlEnabled += 1
        Try
            ComboLocalModel.Items.Clear()
            For i = 0 To AiTranslateModels.Length - 1
                Dim Parts As String() = AiTranslateModels(i).Split("|"c)
                If Parts.Length < 4 Then Continue For
                ComboLocalModel.Items.Add(New MyComboBoxItem With {
                    .Content = $"{Parts(2)} · {Parts(3)} MB",
                    .Tag = i})
            Next
            Dim Current As Integer = Settings.Get(Of Integer)("AiTranslateModel")
            If Current >= 0 AndAlso Current < ComboLocalModel.Items.Count Then
                ComboLocalModel.SelectedIndex = Current
            ElseIf ComboLocalModel.Items.Count > 0 Then
                ComboLocalModel.SelectedIndex = 0
            End If
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    ''' <summary>切换翻译模型。</summary>
    Private Sub ComboLocalModel_SelectionChanged() Handles ComboLocalModel.SelectionChanged
        If AniControlEnabled > 0 Then Return
        Dim Item As MyComboBoxItem = TryCast(ComboLocalModel.SelectedItem, MyComboBoxItem)
        If Item Is Nothing Then Return
        Settings.Set("AiTranslateModel", CInt(Item.Tag))
        RefreshLocalAiStatus()
    End Sub

End Class
