Imports System.Windows.Documents
Imports System.Windows.Media
Public Class PageSetupSystem


    Private Sub PageSetupSystem_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        If BuildType = BuildTypes.Release Then
            PanDonate.Visibility = Visibility.Collapsed
        Else
            PanDonate.Visibility = Visibility.Visible
            ItemSystemUpdateDownload.Content = "在有新版本时自动下载（更新快照版前需要先输入土豆码）"
        End If

        '非重复加载部分
        Static IsLoaded As Boolean = False
        If IsLoaded Then Return
        IsLoaded = True

        AniControlEnabled += 1
        Reload()
        SliderLoad()
        AniControlEnabled -= 1

        '刷新日志列表
        InitLogList()
    End Sub
    Public Sub Reload()
        SettingService.RefreshSettings(Me)
    End Sub
    Public Sub Reset()
        Try
            SettingService.ResetSettings(Me)
            Logger.Info("已初始化启动器页设置")
            Hint("已初始化启动器页设置！", HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "初始化启动器页设置失败", LogBehavior.Alert)
        End Try
        Reload()
    End Sub

    '滑动条
    Private Sub SliderLoad()
        SliderDownloadThread.GetHintText = Function(v) v + 1
        SliderDownloadSpeed.GetHintText =
        Function(v)
            Select Case v
                Case Is <= 14
                    Return (v + 1) * 0.1 & " M/s"
                Case Is <= 31
                    Return (v - 11) * 0.5 & " M/s"
                Case Is <= 41
                    Return (v - 21) & " M/s"
                Case Else
                    Return "无限制"
            End Select
        End Function
        SliderDebugAnim.GetHintText = Function(v) If(v > 29, "关闭", (v / 10 + 0.1) & "x")
    End Sub
    Private Sub SliderDownloadThread_PreviewChange(sender As Object, e As RouteEventArgs) Handles SliderDownloadThread.PreviewChange
        If SliderDownloadThread.Value < 100 Then Return
        If Not Settings.Get(Of Boolean)("HintDownloadThread") Then
            Settings.Set("HintDownloadThread", True)
            MyMsgBox("如果设置过多的下载线程，可能会导致下载时出现非常严重的卡顿。" & vbCrLf &
                     "一般设置 64 线程即可满足大多数下载需求，除非你知道你在干什么，否则不建议设置更多的线程数！", "警告", "我知道了", IsWarn:=True)
        End If
    End Sub

    '识别码/土豆码替代入口
    Private Sub BtnSystemIdentify_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemIdentify.Click
        PageOtherAbout.CopyIdentify()
    End Sub
    Private Sub BtnSystemUnlock_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemUnlock.Click
        InputPotatoCode(False)
    End Sub

    '清除所有配置
    Private Sub BtnSystemClearAllConfig_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemClearAllConfig.Click
        If MyMsgBox("即将清除 PCL 的所有配置数据。" & vbCrLf & vbCrLf &
                    "将会删除：" & vbCrLf &
                    "· 全部设置项（个性化、启动、下载、系统等）" & vbCrLf &
                    "· 登录信息与账号缓存" & vbCrLf &
                    "· 自定义事件与主页配置" & vbCrLf &
                    "· 各个版本的独立设置" & vbCrLf & vbCrLf &
                    "清除后 PCL 会恢复到初次启动的状态，且无法撤销。是否继续？",
                    "清除所有配置", "继续", "取消", "", IsWarn:=True) = 2 Then Return
        If MyMsgBox("这是最后一次确认！" & vbCrLf & "清除后所有配置都无法恢复，确定要继续吗？",
                    "最后确认", "清除所有配置", "取消", "", IsWarn:=True) = 2 Then Return
        RunInNewThread(
        Sub()
            Try
                Dim Count As Integer = 0
                '1. 注册表中的全部配置
                Try
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree($"Software\{RegFolder}", False)
                    Count += 1
                    Logger.Info($"已清除注册表配置：HKCU\Software\{RegFolder}")
                Catch ex As Exception
                    Logger.Warn(ex, "清除注册表配置失败")
                End Try
                '2. 全局配置文件
                For Each File As String In {Paths.Base & "PCL\Setup.ini", Paths.Base & "PCL\PCL.ini"}
                    If FileUtils.Exists(File) Then
                        FileUtils.Delete(File, False)
                        Count += 1
                        Logger.Info($"已清除配置文件：{File}")
                    End If
                Next
                '3. 各个版本的独立设置
                If Not McFolderList.Any() Then McFolderListLoader.WaitForExit()
                For Each Folder As McFolder In McFolderList
                    Dim VersionDir As String = Folder.Location & "versions"
                    If Not DirectoryUtils.Exists(VersionDir) Then Continue For
                    For Each Version As String In DirectoryUtils.EnumerateDirectories(VersionDir, False, "*")
                        Dim IniPath As String = IO.Path.Combine(Version, "PCL\Setup.ini")
                        If FileUtils.Exists(IniPath) Then
                            FileUtils.Delete(IniPath, False)
                            Count += 1
                        End If
                    Next
                Next
                '4. 缓存目录（含主页自定义等配置数据）
                If DirectoryUtils.Exists(PathTemp) Then
                    DirectoryUtils.Delete(PathTemp, False)
                    Count += 1
                    Logger.Info($"已清除缓存目录：{PathTemp}")
                End If
                Logger.Info($"清除所有配置完成，共处理 {Count} 项")
                RunInUi(
                Sub()
                    MyMsgBox("所有配置已清除完毕。" & vbCrLf & "PCL 将立即重启以应用更改。", "清除完成", "重启 PCL")
                    StartProcess(PathExe)
                    FormMain.EndProgramForce()
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, "清除所有配置失败", LogBehavior.Alert)
            End Try
        End Sub, "Clear All Config")
    End Sub

    '调试模式
    Private Sub CheckDebugMode_Change() Handles CheckDebugMode.Change
        If AniControlEnabled = 0 Then Hint("部分调试信息将在刷新或启动器重启后切换显示！",, False)
    End Sub

    '自动更新
    Private Sub ComboSystemActivity_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboSystemActivity.SelectionChanged
        If AniControlEnabled <> 0 Then Return
        If ComboSystemActivity.SelectedIndex <> 2 Then Return
        If MyMsgBox("若选择此项，即使在将来出现严重问题时，你也无法获取相关通知。" & vbCrLf &
                    "例如，如果发现某个版本游戏存在严重 Bug，你可能就会因为无法得到通知而导致无法预知的后果。" & vbCrLf & vbCrLf &
                    "一般选择 仅在有重要通知时显示公告 就可以让你尽量不受打扰了。" & vbCrLf &
                    "除非你在制作服务器整合包，或时常手动更新启动器，否则极度不推荐选择此项！", "警告", "我知道我在做什么", "取消", IsWarn:=True) = 2 Then
            If e.RemovedItems.Count > 0 Then ComboSystemActivity.SelectedItem = e.RemovedItems(0)
        End If
    End Sub
    Private Sub ComboSystemUpdate_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboSystemUpdate.SelectionChanged
        If AniControlEnabled <> 0 Then Return
        If ComboSystemUpdate.SelectedIndex <> 3 Then Return
        If MyMsgBox("若选择此项，即使在启动器将来出现严重问题时，你也无法获取更新并获得修复。" & vbCrLf &
                    "例如，如果官方修改了登录方式，从而导致现有启动器无法登录，你可能就会因为无法更新而无法开始游戏。" & vbCrLf & vbCrLf &
                    "一般选择 仅在有重大漏洞更新时显示提示 就可以让你尽量不受打扰了。" & vbCrLf &
                    "除非你在制作服务器整合包，或时常手动更新启动器，否则极度不推荐选择此项！", "警告", "我知道我在做什么", "取消", IsWarn:=True) = 2 Then
            If e.RemovedItems.Count > 0 Then ComboSystemUpdate.SelectedItem = e.RemovedItems(0)
        End If
    End Sub
    Private Sub BtnSystemUpdate_Click(sender As Object, e As EventArgs) Handles BtnSystemUpdate.Click
        UpdateCheckByButton()
    End Sub
    ''' <summary>
    ''' 启动器是否已经是最新版？
    ''' 若返回 Nothing，则代表无更新缓存文件或出错。
    ''' </summary>
    Public Shared Function IsLauncherNewest() As Boolean?
        Try
            '确认服务器公告是否正常
            Dim ServerContent As String = If(FileUtils.TryReadAsString(PathTemp & "Cache\Notice.cfg"), "")
            If ServerContent.Split("|").Count < 3 Then Return Nothing
            '确认是否为最新
            Return ServerContent.Split("|")(If(BuildType = BuildTypes.Release, 2, 1)) <= VersionCode
        Catch ex As Exception
            Logger.Error(ex, "确认启动器更新失败")
            Return Nothing
        End Try
    End Function

#Region "导出 / 导入设置"

    Private Sub BtnSystemSettingExp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingExp.Click
        Hint("该功能尚在开发中！")
    End Sub
    Private Sub BtnSystemSettingImp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingImp.Click
        Hint("该功能尚在开发中！")
    End Sub

#End Region


#Region "卡片：运行日志"

    ''' <summary>当前选中的日志文件。</summary>
    Private SelectedLog As String = Nothing

    ''' <summary>日志所在文件夹。</summary>
    Private ReadOnly Property LogFolder As String
        Get
            Return Paths.Base & "PCL\"
        End Get
    End Property

    ''' <summary>扫描并显示启动器日志文件。</summary>
    Public Sub InitLogList()
        If PanLogList Is Nothing Then Return
        PanLogList.Children.Clear()
        SelectedLog = Nothing
        If Not IO.Directory.Exists(LogFolder) Then
            LabLogHint.Text = "还没有生成日志文件。"
            Return
        End If
        Dim Files As String() = IO.Directory.GetFiles(LogFolder, "Log*.txt")
        If Files.Length = 0 Then
            LabLogHint.Text = "还没有生成日志文件。"
            Return
        End If
        Dim Ordered = Files.OrderByDescending(Function(f) IO.File.GetLastWriteTime(f)).ToList()
        LabLogHint.Text = $"共 {Ordered.Count} 份日志，最近一份是 {IO.File.GetLastWriteTime(Ordered(0)):yyyy-MM-dd HH:mm}。选中后可查看或导出。"
        For Each LogFile As String In Ordered
            Dim LogPath As String = LogFile '闭包捕获
            Dim Info As New IO.FileInfo(LogPath)
            Dim Item As New MyListItem With {
                .Type = MyListItem.CheckType.RadioBox,
                .Height = 44,
                .Title = Info.Name,
                .Info = $"{FormatLogSize(Info.Length)} · {Info.LastWriteTime:yyyy-MM-dd HH:mm:ss}"}
            AddHandler Item.Check,
                Sub(sender As Object, e As RouteEventArgs)
                    SelectedLog = LogPath
                End Sub
            PanLogList.Children.Add(Item)
        Next
    End Sub

    Private Shared Function FormatLogSize(Size As Long) As String
        If Size >= 1024L * 1024 Then Return $"{Size / 1024.0 / 1024:0.##} MB"
        If Size >= 1024 Then Return $"{Size / 1024.0:0.#} KB"
        Return $"{Size} B"
    End Function

    Private Sub BtnLogRefresh_Click() Handles BtnLogRefresh.Click
        InitLogList()
        Hint("日志列表已刷新")
    End Sub

    Private Sub BtnLogFolder_Click() Handles BtnLogFolder.Click
        DirectoryUtils.Create(LogFolder)
        OpenExplorer(LogFolder)
    End Sub

    Private Sub BtnLogView_Click() Handles BtnLogView.Click
        If SelectedLog Is Nothing Then
            Hint("请先选中一份日志！", HintType.Red)
            Return
        End If
        Try
            Dim Content As String = FileUtils.ReadAsString(SelectedLog)
            Const MaxLength As Integer = 6000
            If Content.Length > MaxLength Then
                Content = "……（日志过长，仅显示最后 " & MaxLength & " 个字符，完整内容请点「导出选中日志」）……" & vbCrLf & vbCrLf &
                          Content.Substring(Content.Length - MaxLength)
            End If
            MyMsgBox(Content, $"日志：{IO.Path.GetFileName(SelectedLog)}")
        Catch ex As Exception
            Hint($"读取日志失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

    Private Sub BtnLogExport_Click() Handles BtnLogExport.Click
        If SelectedLog Is Nothing Then
            Hint("请先选中一份日志！", HintType.Red)
            Return
        End If
        Dim Target As String = Dialogs.SaveFile("选择日志保存位置", IO.Path.GetFileName(SelectedLog), filter:={("txt", "文本文件")})
        If Target Is Nothing Then Return
        Try
            FileUtils.Copy(SelectedLog, Target)
            Hint($"日志已导出到 {Target}", HintType.Green)
        Catch ex As Exception
            Hint($"导出失败：{ex.Message}", HintType.Red)
        End Try
    End Sub

#End Region

End Class
