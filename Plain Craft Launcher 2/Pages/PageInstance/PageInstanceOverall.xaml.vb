Public Class PageInstanceOverall

    Private IsLoad As Boolean = False
    Private Sub PageSetupLaunch_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        '更新设置
        Reload()
        '刷新存档列表
        InitSavesList()
        '刷新截图列表
        InitShotsList()

        '非重复加载部分
        If IsLoad Then Return
        IsLoad = True
        PanDisplay.TriggerForceResize()

    End Sub

    Public InstanceHeaderItem As MyListItem
    ''' <summary>
    ''' 确保当前页面上的信息已正确显示。
    ''' </summary>
    Private Sub Reload()
        AniControlEnabled += 1

        '刷新设置项目
        ComboDisplayType.SelectedIndex = ReadIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "DisplayType", McInstanceCardType.Auto)
        BtnDisplayStar.Text = If(PageInstanceLeft.Instance.IsStar, "从收藏夹中移除", "加入收藏夹")
        BtnFolderMods.Visibility = If(PageInstanceLeft.Instance.Modable, Visibility.Visible, Visibility.Collapsed)
        '刷新版本显示
        PanDisplayItem.Children.Clear()
        InstanceHeaderItem = PageInstanceLeft.Instance.ToListItem().Init()
        InstanceHeaderItem.IsHitTestVisible = False
        PanDisplayItem.Children.Add(InstanceHeaderItem)
        FrmMain.PageNameRefresh()
        '刷新版本图标
        ComboDisplayLogo.SelectedIndex = 0
        Dim Logo As String = ReadIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "Logo", "")
        Dim LogoCustom As Boolean = ReadIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "LogoCustom", "False")
        If LogoCustom Then
            For Each Selection As MyComboBoxItem In ComboDisplayLogo.Items
                If Selection.Tag = Logo OrElse (Selection.Tag = "PCL\Logo.png" AndAlso Logo.EndsWithF("PCL\Logo.png")) Then
                    ComboDisplayLogo.SelectedItem = Selection
                    Exit For
                End If
            Next
        End If

        AniControlEnabled -= 1
    End Sub

#Region "卡片：个性化"

    '版本分类
    Private Sub ComboDisplayType_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboDisplayType.SelectionChanged
        If Not (IsLoad AndAlso AniControlEnabled = 0) Then Return
        If ComboDisplayType.SelectedIndex <> 1 Then
            '改为不隐藏
            Try
                '若设置分类为可安装 Mod，则显示正常的 Mod 管理页面
                WriteIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "DisplayType", ComboDisplayType.SelectedIndex)
                PageInstanceLeft.Instance.DisplayType = ReadIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "DisplayType", McInstanceCardType.Auto)
                FrmInstanceLeft.RefreshModDisabled()

                WriteIni(McFolderSelected & "PCL.ini", "InstanceCache", "") '要求刷新缓存
                LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            Catch ex As Exception
                Logger.Error(ex, $"修改版本分类失败（{PageInstanceLeft.Instance.Name}）")
            End Try
            Reload() '更新 “打开 Mod 文件夹” 按钮
        Else
            '改为隐藏
            Try
                If Not Settings.Get(Of Boolean)("HintHide") Then
                    If MyMsgBox("确认要从版本列表中隐藏该版本吗？隐藏该版本后，它将不再出现于 PCL 显示的版本列表中。" & vbCrLf & "此后，在版本列表页面按下 F11 才可以查看被隐藏的版本。", "隐藏版本提示",, "取消") <> 1 Then
                        ComboDisplayType.SelectedIndex = 0
                        Return
                    End If
                    Settings.Set("HintHide", True)
                End If
                WriteIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "DisplayType", McInstanceCardType.Hidden)
                WriteIni(McFolderSelected & "PCL.ini", "InstanceCache", "") '要求刷新缓存
                LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            Catch ex As Exception
                Logger.Error(ex, $"隐藏版本 {PageInstanceLeft.Instance.Name} 失败")
            End Try
        End If
    End Sub

    '更改描述
    Private Sub BtnDisplayDesc_Click(sender As Object, e As EventArgs) Handles BtnDisplayDesc.Click
        Try
            Dim OldInfo As String = ReadIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "CustomInfo")
            Dim NewInfo As String = MyMsgBoxInput("更改描述", "修改版本的描述文本，留空则使用 PCL 的默认描述。", OldInfo, New ObjectModel.Collection(Of Validate), "默认描述")
            If NewInfo IsNot Nothing AndAlso OldInfo <> NewInfo Then WriteIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "CustomInfo", NewInfo)
            PageInstanceLeft.Instance = New McInstance(PageInstanceLeft.Instance.Name).Load()
            Reload()
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
        Catch ex As Exception
            Logger.Error(ex, $"版本 {PageInstanceLeft.Instance.Name} 描述更改失败", LogBehavior.Alert)
        End Try
    End Sub

    '重命名版本
    Private Sub BtnDisplayRename_Click(sender As Object, e As EventArgs) Handles BtnDisplayRename.Click
        Try
            If McInstanceLock.IsLocked(PageInstanceLeft.Instance) Then
                Hint("该版本正在被修改，请稍后再重命名！", HintType.Red)
                Return
            End If
            '确认输入的新名称
            Dim OldName As String = PageInstanceLeft.Instance.Name
            '修改此部分的同时修改快速安装的版本名检测*
            Dim NewName As String = MyMsgBoxInput("重命名版本", "", OldName, New ObjectModel.Collection(Of Validate) From {
                New ValidateFolderName(McFolderSelected & "versions", IgnoreList:=New List(Of String) From {OldName})
            })
            If String.IsNullOrWhiteSpace(NewName) Then Return
            Dim NewInstance = PageInstanceLeft.Instance.Rename(NewName)
            '刷新与提示
            Hint("重命名成功！", HintType.Green)
            PageInstanceLeft.Instance = NewInstance
            If Not IsNothing(McInstanceSelected) AndAlso McInstanceSelected.Equals(PageInstanceLeft.Instance) Then WriteIni(McFolderSelected & "PCL.ini", "Version", NewName)
            Reload()
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
        Catch ex As Exception
            Logger.Error(ex, "重命名版本失败", LogBehavior.Alert)
        End Try
    End Sub

    '版本图标
    Private Sub ComboDisplayLogo_SelectionChanged() Handles ItemDisplayLogoCustom.PreviewMouseLeftButtonUp, ComboDisplayLogo.SelectionChanged
        If Not (IsLoad AndAlso AniControlEnabled = 0) Then Return
        '选择 自定义 时修改图片
        Try
            If ComboDisplayLogo.SelectedItem Is ItemDisplayLogoCustom Then
                Dim FileName As String = Dialogs.SelectFile("选择图片", False, filter:={({"png", "jpeg", "jpg", "gif", "webp"}, "常用图片文件")}).FirstOrDefault()
                If String.IsNullOrEmpty(FileName) Then
                    Reload() '还原选项
                    Return
                End If
                FileUtils.Copy(FileName, PageInstanceLeft.Instance.PathVersion & "PCL\Logo.png")
            Else
                FileUtils.Delete(PageInstanceLeft.Instance.PathVersion & "PCL\Logo.png")
            End If
        Catch ex As Exception
            Logger.Error(ex, $"更改自定义版本图标失败（{PageInstanceLeft.Instance.Name}）")
        End Try
        '进行更改
        Try
            Dim NewLogo As String = ComboDisplayLogo.SelectedItem.Tag
            WriteIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "Logo", NewLogo)
            WriteIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "LogoCustom", Not NewLogo = "")
            '刷新显示
            WriteIni(McFolderSelected & "PCL.ini", "InstanceCache", "") '要求刷新缓存
            PageInstanceLeft.Instance = New McInstance(PageInstanceLeft.Instance.Name).Load()
            Reload()
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
        Catch ex As Exception
            Logger.Error(ex, $"更改版本图标失败（{PageInstanceLeft.Instance.Name}）")
        End Try
    End Sub

    '收藏夹
    Private Sub BtnDisplayStar_Click(sender As Object, e As EventArgs) Handles BtnDisplayStar.Click
        Try
            WriteIni(PageInstanceLeft.Instance.PathVersion & "PCL\Setup.ini", "IsStar", Not PageInstanceLeft.Instance.IsStar)
            PageInstanceLeft.Instance = New McInstance(PageInstanceLeft.Instance.Name).Load()
            Reload()
            McInstanceListForceRefresh = True
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
        Catch ex As Exception
            Logger.Error(ex, $"版本 {PageInstanceLeft.Instance.Name} 收藏状态更改失败", LogBehavior.Alert)
        End Try
    End Sub

#End Region

#Region "卡片：快捷方式"

    '版本文件夹
    Private Sub BtnFolderVersion_Click() Handles BtnFolderVersion.Click
        OpenInstanceFolder(PageInstanceLeft.Instance)
    End Sub
    Public Shared Sub OpenInstanceFolder(Instance As McInstance)
        OpenExplorer(Instance.PathVersion)
    End Sub

    '存档文件夹
    Private Sub BtnFolderSaves_Click() Handles BtnFolderSaves.Click
        Dim Folder As String = PageInstanceLeft.Instance.PathIndie & "saves\"
        DirectoryUtils.Create(Folder)
        OpenExplorer(Folder)
    End Sub

    'Mod 文件夹
    Private Sub BtnFolderMods_Click() Handles BtnFolderMods.Click
        Dim Folder As String = PageInstanceLeft.Instance.PathIndie & "mods\"
        DirectoryUtils.Create(Folder)
        OpenExplorer(Folder)
    End Sub

    '截图文件夹
    Private Sub BtnFolderScreen_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnFolderScreen.Click
        Dim Folder As String = PageInstanceLeft.Instance.PathIndie & "screenshots\"
        DirectoryUtils.Create(Folder)
        OpenExplorer(Folder)
    End Sub

#End Region

#Region "卡片：管理"

    '导出启动脚本
    Private Sub BtnManageScript_Click() Handles BtnManageScript.Click
        Try
            '弹窗要求指定脚本的保存位置
            Dim SavePath As String = Dialogs.SaveFile("选择脚本保存位置", "启动 " & PageInstanceLeft.Instance.Name & ".bat", filter:={("bat", "批处理文件")})
            If SavePath Is Nothing Then Return
            '检查中断（等玩家选完弹窗指不定任务就结束了呢……）
            If McLaunchLoader.State = LoadState.Loading Then
                Hint("请在当前启动任务结束后再试！", HintType.Red)
                Return
            End If
            '生成脚本
            If McLaunchStart(New McLaunchOptions With {.SaveBatch = SavePath, .Instance = PageInstanceLeft.Instance}) Then
                If Settings.Get(Of McLoginType)("LoginType") = McLoginType.Legacy Then
                    Hint("正在导出启动脚本……")
                Else
                    Hint("正在导出启动脚本……（注意，使用脚本启动可能会导致登录失效！）")
                End If
            End If
        Catch ex As Exception
            Logger.Error(ex, $"导出启动脚本失败（{PageInstanceLeft.Instance.Name}）", LogBehavior.Alert)
        End Try
    End Sub

    '补全文件
    Private Sub BtnManageCheck_Click(sender As Object, e As EventArgs) Handles BtnManageCheck.Click
        Try
            If McInstanceLock.IsLocked(PageInstanceLeft.Instance) Then
                Hint("该版本正在被修改，请稍后再补全文件！", HintType.Red)
                Return
            End If
            '忽略文件检查提示
            If ShouldIgnoreFileCheck(PageInstanceLeft.Instance) Then
                Hint("请先关闭 [版本设置 → 设置 → 高级启动选项 → 关闭文件校验]，然后再尝试补全文件！", HintType.Blue)
                Return
            End If
            '重复任务检查
            For Each OngoingLoader In LoaderTaskbar
                If OngoingLoader.Name <> PageInstanceLeft.Instance.Name & " 文件补全" Then Continue For
                Hint("正在处理中，请稍候！", HintType.Red)
                Return
            Next
            '启动
            Dim Loader As New LoaderCombo(Of String)(PageInstanceLeft.Instance.Name & " 文件补全", DlClientFix(PageInstanceLeft.Instance, True, False))
            Loader.OnStateChanged =
            Sub()
                Select Case Loader.State
                    Case LoadState.Finished
                        Hint(Loader.Name & "成功！", HintType.Green)
                    Case LoadState.Failed
                        Hint(Loader.Name & "失败：" & Loader.Error.GetDisplay(False), HintType.Red)
                    Case LoadState.Canceled
                        Hint(Loader.Name & "已取消！", HintType.Blue)
                End Select
            End Sub
            Loader.Start(PageInstanceLeft.Instance.Name)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
        Catch ex As Exception
            Logger.Error(ex, $"尝试补全文件失败（{PageInstanceLeft.Instance.Name}）", LogBehavior.Alert)
        End Try
    End Sub

    '删除版本
    '修改此代码时，同时修改 PageSelectRight 中的代码
    Private Sub BtnManageDelete_Click(sender As Object, e As EventArgs) Handles BtnManageDelete.Click
        Try
            If McInstanceLock.IsLocked(PageInstanceLeft.Instance) Then
                Hint("该版本正在被修改，请稍后再删除！", HintType.Red)
                Return
            End If
            Dim IsShiftPressed As Boolean = My.Computer.Keyboard.ShiftKeyDown
            Dim IsHintIndie As Boolean = PageInstanceLeft.Instance.State <> McInstance.McInstanceState.Error AndAlso PageInstanceLeft.Instance.PathIndie <> McFolderSelected
            Dim SavesFolder = DirectoryUtils.GetInfo(PageInstanceLeft.Instance.PathIndie & "saves\")
            Dim SaveEntries = If(IsHintIndie AndAlso SavesFolder.Exists, SavesFolder.EnumerateDirectories.OrderByDescending(Function(Save) Save.LastWriteTime).Select(Function(Save) $"{Save.Name}（上次修改：{StringUtils.FormatTimeSpan(Save.LastWriteTime - Date.Now, False)}）").ToList, New List(Of String))
            Select Case MyMsgBox($"你确定要{If(IsShiftPressed, "永久", "")}删除版本 {PageInstanceLeft.Instance.Name} 吗？" &
                        If(IsHintIndie, vbCrLf & "该版本对应的存档、资源包、Mod 等文件也将被一并删除！" &
                            If(SaveEntries.Any, vbCrLf & vbCrLf & "这会删除以下存档：" & vbCrLf & "· " & SaveEntries.Join(vbCrLf & "· "), ""), ""),
                        "版本删除确认", , "取消",, IsHintIndie OrElse IsShiftPressed)
                Case 1
                    PageInstanceLeft.Instance.ResetSettingsCache()
                    DirectoryUtils.Delete(PageInstanceLeft.Instance.PathVersion, Not IsShiftPressed)
                    Hint("已删除版本 " & PageInstanceLeft.Instance.Name & "！", HintType.Green)
                Case 2
                    Return
            End Select
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            FrmMain.PageBack()
        Catch ex As OperationCanceledException
            Logger.Warn(ex, $"删除版本 {PageInstanceLeft.Instance.Name} 被主动取消")
        Catch ex As Exception
            Logger.Error(ex, $"删除版本 {PageInstanceLeft.Instance.Name} 失败", LogBehavior.Alert)
        End Try
    End Sub

#End Region


#Region "卡片：存档管理"

    ''' <summary>当前选中的存档目录。</summary>
    Private SelectedSave As String = Nothing

    ''' <summary>扫描并显示版本存档目录下的所有存档。</summary>
    Public Sub InitSavesList()
        If PanSavesList Is Nothing Then Return
        PanSavesList.Children.Clear()
        SelectedSave = Nothing
        Dim Folder As String = PageInstanceLeft.Instance.PathIndie & "saves\"
        If Not IO.Directory.Exists(Folder) Then
            LabSavesHint.Text = "还没有任何存档。进入游戏创建一个世界后，这里就会列出来。"
            Return
        End If
        Dim Dirs As String() = IO.Directory.GetDirectories(Folder)
        If Dirs.Length = 0 Then
            LabSavesHint.Text = "还没有任何存档。"
            Return
        End If
        LabSavesHint.Text = $"共 {Dirs.Length} 个存档。选中一个后可以备份或删除；备份会放进 saves_backup 文件夹。"
        For Each Dir As String In Dirs.OrderByDescending(Function(d) IO.Directory.GetLastWriteTime(d))
            Dim SavePath As String = Dir '闭包捕获
            Dim Name As String = IO.Path.GetFileName(SavePath)
            Dim Size As Long = 0
            Try
                Size = New IO.DirectoryInfo(SavePath).EnumerateFiles("*", IO.SearchOption.AllDirectories).Sum(Function(f) f.Length)
            Catch
            End Try
            Dim Item As New MyListItem With {
                .Type = MyListItem.CheckType.RadioBox,
                .Height = 44,
                .Title = Name,
                .Info = $"{FormatSaveSize(Size)} · 最后修改 {IO.Directory.GetLastWriteTime(SavePath):yyyy-MM-dd HH:mm}"}
            AddHandler Item.Check,
                Sub(sender As Object, e As RouteEventArgs)
                    SelectedSave = SavePath
                End Sub
            PanSavesList.Children.Add(Item)
        Next
    End Sub

    Private Shared Function FormatSaveSize(Size As Long) As String
        If Size >= 1024L * 1024 * 1024 Then Return $"{Size / 1024.0 / 1024 / 1024:0.##} GB"
        If Size >= 1024L * 1024 Then Return $"{Size / 1024.0 / 1024:0.##} MB"
        If Size >= 1024 Then Return $"{Size / 1024.0:0.#} KB"
        Return $"{Size} B"
    End Function

    Private Sub BtnSavesRefresh_Click() Handles BtnSavesRefresh.Click
        InitSavesList()
        Hint("存档列表已刷新")
    End Sub

    Private Sub BtnSavesBackup_Click() Handles BtnSavesBackup.Click
        If SelectedSave Is Nothing Then
            Hint("请先选中一个存档！", HintType.Red)
            Return
        End If
        Dim Name As String = IO.Path.GetFileName(SelectedSave)
        Dim Target As String = PageInstanceLeft.Instance.PathIndie & "saves_backup\" & Name & "_" & Date.Now.ToString("yyyyMMdd_HHmmss") & "\"
        Hint("正在备份存档……")
        RunInThread(
        Sub()
            Try
                CopySaveDirectory(SelectedSave, Target)
                RunInUi(Sub() Hint($"已备份到 {Target}", HintType.Green))
            Catch ex As Exception
                RunInUi(Sub() Hint($"备份失败：{ex.Message}", HintType.Red))
            End Try
        End Sub)
    End Sub

    Private Shared Sub CopySaveDirectory(Source As String, Target As String)
        IO.Directory.CreateDirectory(Target)
        For Each File As String In IO.Directory.GetFiles(Source)
            IO.File.Copy(File, IO.Path.Combine(Target, IO.Path.GetFileName(File)), True)
        Next
        For Each Dir As String In IO.Directory.GetDirectories(Source)
            CopySaveDirectory(Dir, IO.Path.Combine(Target, IO.Path.GetFileName(Dir)))
        Next
    End Sub

    Private Sub BtnSavesDelete_Click() Handles BtnSavesDelete.Click
        If SelectedSave Is Nothing Then
            Hint("请先选中一个存档！", HintType.Red)
            Return
        End If
        Dim Name As String = IO.Path.GetFileName(SelectedSave)
        If MyMsgBox($"确定要删除存档「{Name}」吗？{vbCrLf}{vbCrLf}此操作无法撤销，建议先做一次备份！", "删除存档", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Try
            IO.Directory.Delete(SelectedSave, True)
            Hint("存档已删除", HintType.Green)
            InitSavesList()
        Catch ex As Exception
            Hint($"删除失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

#End Region

#Region "卡片：截图管理"

    ''' <summary>当前选中的截图文件。</summary>
    Private SelectedShot As String = Nothing

    ''' <summary>扫描并显示版本截图目录下的所有截图。</summary>
    Public Sub InitShotsList()
        If PanShotsList Is Nothing Then Return
        PanShotsList.Children.Clear()
        SelectedShot = Nothing
        Dim Folder As String = PageInstanceLeft.Instance.PathIndie & "screenshots\"
        If Not IO.Directory.Exists(Folder) Then
            LabShotsHint.Text = "还没有任何截图。在游戏里按 F2 截图后，这里就会列出来。"
            Return
        End If
        Dim Files As String() = IO.Directory.GetFiles(Folder)
        Dim Shots = Files.Where(Function(f) {".png", ".jpg", ".jpeg", ".bmp"}.Contains(IO.Path.GetExtension(f).ToLowerInvariant())).
                          OrderByDescending(Function(f) IO.File.GetLastWriteTime(f)).ToList()
        If Shots.Count = 0 Then
            LabShotsHint.Text = "还没有任何截图。"
            Return
        End If
        LabShotsHint.Text = $"共 {Shots.Count} 张截图。选中一张后可以查看或删除。"
        For Each Shot As String In Shots
            Dim ShotPath As String = Shot '闭包捕获
            Dim Info As New IO.FileInfo(ShotPath)
            Dim Item As New MyListItem With {
                .Type = MyListItem.CheckType.RadioBox,
                .Height = 44,
                .Logo = ShotPath,
                .Title = Info.Name,
                .Info = $"{FormatSaveSize(Info.Length)} · {Info.LastWriteTime:yyyy-MM-dd HH:mm}"}
            AddHandler Item.Check,
                Sub(sender As Object, e As RouteEventArgs)
                    SelectedShot = ShotPath
                End Sub
            PanShotsList.Children.Add(Item)
        Next
    End Sub

    Private Sub BtnShotsRefresh_Click() Handles BtnShotsRefresh.Click
        InitShotsList()
        Hint("截图列表已刷新")
    End Sub

    Private Sub BtnShotsFolder_Click() Handles BtnShotsFolder.Click
        Dim Folder As String = PageInstanceLeft.Instance.PathIndie & "screenshots\"
        DirectoryUtils.Create(Folder)
        OpenExplorer(Folder)
    End Sub

    Private Sub BtnShotsOpen_Click() Handles BtnShotsOpen.Click
        If SelectedShot Is Nothing Then
            Hint("请先选中一张截图！", HintType.Red)
            Return
        End If
        OpenExplorer(SelectedShot)
    End Sub

    Private Sub BtnShotsDelete_Click() Handles BtnShotsDelete.Click
        If SelectedShot Is Nothing Then
            Hint("请先选中一张截图！", HintType.Red)
            Return
        End If
        Dim Name As String = IO.Path.GetFileName(SelectedShot)
        If MyMsgBox($"确定要删除截图「{Name}」吗？", "删除截图", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Try
            IO.File.Delete(SelectedShot)
            Hint("截图已删除", HintType.Green)
            InitShotsList()
        Catch ex As Exception
            Hint($"删除失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

#End Region
End Class
