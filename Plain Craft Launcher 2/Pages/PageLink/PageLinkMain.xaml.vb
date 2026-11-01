Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Net.Sockets
Imports System.Reflection
Imports PCL

Public Class PageLinkMain
    Private Const INVITE_CODE_VERSION As Integer = 2

    '===============================
    '  状态机与前端页面
    '===============================

#Region "状态管理"

    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum
    Public Shared LinkState As LinkStates = LinkStates.Waiting

    ''' <summary>
    ''' 切换到指定的状态。
    ''' </summary>
    Private Sub ChangeState(NewState As LinkStates)
        If LinkState = NewState Then Return
        Dim OldState = LinkState
        LinkState = NewState
        Logger.Info($"主状态由 {OldState} 变更为 {NewState}")
        '触发状态切换
        RunInUi(
        Sub()
            Select Case NewState
                Case LinkStates.Waiting
                    SwitchToWaiting(OldState)
                Case LinkStates.Loading
                    SwitchToLoading(OldState)
                Case LinkStates.Failed
                    SwitchToFailed(OldState)
                Case LinkStates.Finished
                    SwitchToFinished(OldState)
            End Select
        End Sub)
    End Sub

    '让加载器的 成功/失败 事件触发状态切换
    '联机方式切换
    Private Sub RadioLinkProvider_Change() Handles RadioLinkHongshi.Changed, RadioLinkTerracotta.Changed
        If RadioLinkTerracotta.Checked Then
            LinkProviderNow = ModLink.LinkProvider.Terracotta
            LabLinkProviderHint.Text = "虚拟局域网方式：房主无需填端口（会自动扫描本地游戏），朋友用房间码加入，双方环境由启动器自动准备。"
        Else
            LinkProviderNow = ModLink.LinkProvider.Hongshi
            LabLinkProviderHint.Text = "把本地端口映射到公网，朋友在游戏里直接填地址就能进，无需安装任何东西。"
        End If
    End Sub

    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanContent, PanAlways, LinkLoader, AutoRun:=False)
        AddHandler LinkLoader.OnStateChangedUi,
        Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
            Select Case NewState
                Case LoadState.Finished
                    ChangeState(LinkStates.Finished)
                Case LoadState.Failed
                    Telemetry("联机失败", "Exception", FilterUserName(Loader.Error.GetDisplay(True), "*"))
                    ChangeState(LinkStates.Failed)
            End Select
        End Sub
    End Sub

    '页面动画兼容
    Private Sub UpdatePanelVisibility() Handles Me.PageEnter
        FrmLinkMain.PanSelect.Visibility = If(LinkState = LinkStates.Waiting, Visibility.Visible, Visibility.Collapsed)
        '首次进入时默认选中红石联机
        If LinkState = LinkStates.Waiting AndAlso Not RadioLinkHongshi.Checked AndAlso Not RadioLinkTerracotta.Checked Then RadioLinkHongshi.Checked = True
        FrmLinkMain.PanFinish.Visibility = If(LinkState = LinkStates.Finished, Visibility.Visible, Visibility.Collapsed)
    End Sub

#End Region

#Region "初始页面 (Waiting)"

    '由任意状态切换到 Waiting
    Private Sub SwitchToWaiting(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        SyncLock LinkLoader.LockState
            LinkLoader.State = LoadState.Waiting
        End SyncLock
        ProcessStop()
        '页面切换
        If OldState <> LinkStates.Finished Then
            '从完成页面退出时，PageOnContentExit 结束会触发 PageEnter 事件，这会调用 UpdatePanelVisibility
            '如果在现在提前调用了，会丢失完成页面的退出动画
            UpdatePanelVisibility()
        End If
        PageOnContentExit()
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '创建
    Private Sub Create_MouseLeftButtonUp() Handles PanSelectCreate.MouseLeftButtonUp
        IsServerSide = True
        If LinkProviderNow = ModLink.LinkProvider.Hongshi Then
            '红石需要知道本地 Minecraft 的局域网端口
            Dim Choice = MyMsgBox("请先在 Minecraft 单人游戏里按 Esc 选择「对局域网开放」。" & vbCrLf & vbCrLf &
                                  "是否自动检测游戏开放的端口？", "创建房间", "自动检测", "手动输入")
            If Choice = 2 Then
                Dim Port As String = MyMsgBoxInput("输入端口", "输入 Minecraft 聊天栏里显示的端口号（形如 5xxxx）。",
                    ValidateRules:=New Collection(Of Validate) From {New ValidateInteger(1024, 65535)},
                    HintText:="端口号")
                If Port Is Nothing Then Return
                ServerPort = Port
                LinkAutoDetectPort = False
            Else
                ServerPort = 0
                LinkAutoDetectPort = True
            End If
        Else
            'Terracotta 会自己扫描本地 Minecraft 服务器
            ServerPort = 0
            LinkAutoDetectPort = False
        End If
        Logger.Info($"尝试创建联机房间，方式 {LinkProviderNow}，端口 {ServerPort}（自动检测：{LinkAutoDetectPort}）")
        ChangeState(LinkStates.Loading)
    End Sub
    '加入
    ''' <summary>
    ''' 按当前联机方式走不同的加入流程：
    ''' 红石是单边隧道，加入方直接在 Minecraft 里连地址即可，不需要启动器参与；
    ''' Terracotta 需要启动器帮忙建立虚拟局域网，因此走加载流程。
    ''' </summary>
    Public Shared Sub Join() Handles PanSelectJoin.MouseLeftButtonUp
        If LinkProviderNow = ModLink.LinkProvider.Terracotta Then
            Dim Code As String = MyMsgBoxInput("输入房间码", "输入房主发给你的房间码，形如 U/98XV-E6PK-MTND-CWZ5。",
                HintText:="房间码")
            If String.IsNullOrEmpty(Code) Then Return
            If FrmLinkMain IsNot Nothing Then FrmLinkMain.JoinInternal(Code)
        Else
            Dim Address As String = MyMsgBoxInput("输入房间地址", "输入房主发给你的地址（形如 nj.hongshi.site:31310）。",
                HintText:="服务器地址")
            If String.IsNullOrEmpty(Address) Then Return
            Address = Address.Trim
            ClipboardSet(Address, False)
            Settings.Set("LinkLastAutoJoinInviteCode", Address)
            MyMsgBox("地址已复制！" & vbCrLf & vbCrLf &
                     "请在 Minecraft 的多人游戏中添加服务器，地址填写：" & vbCrLf & Address & vbCrLf & vbCrLf &
                     "红石联机不需要双方安装额外软件，直接在游戏里连接即可。", "如何加入房间", "我知道了")
        End If
    End Sub
    Public Sub JoinInternal(Code As String)
        If Code Is Nothing Then Return
        If LinkProviderNow = ModLink.LinkProvider.Hongshi Then Return
        'Terracotta 房间码校验
        Code = Code.Trim.ToUpper.Replace(" ", "")
        If Not System.Text.RegularExpressions.Regex.IsMatch(Code, "^U/[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$") Then
            Hint("房间码格式有误，应形如 U/98XV-E6PK-MTND-CWZ5", HintType.Red)
            Return
        End If
        IsServerSide = False
        LinkJoinCode = Code
        Logger.Info($"尝试加入 Terracotta 房间：{Code}")
        ChangeState(LinkStates.Loading)
    End Sub
    Public Shared Function ValidateCodeFormat(Code As String) As String
        If Code Is Nothing Then Return "邀请码为空！"
        Code = FixCodeFormat(Code)
        '判断类型
        If Not (Code.Length >= 14 AndAlso Code(0) = "P"c AndAlso Code(5) = "-"c AndAlso Code(11) = "-"c) Then
            If Code.StartsWithF("U/") Then 'HMCL
                Return "请让房主使用 PCL 创建房间！"
            ElseIf Code.Length = 10 Then 'PCL CE
                Return "请让房主使用非社区版的 PCL 创建房间！"
            Else
                Return "邀请码有误，请让房主使用 PCL 创建房间！"
            End If
        End If
        '校验版本
        If Code.Length >= 23 AndAlso Code(17) = "-"c AndAlso
            Val(Code.Substring(18, 2)) > INVITE_CODE_VERSION Then Return "你的 PCL 版本太老了，请在更新 PCL 之后再联机！"
        Return Nothing
    End Function
    Private Shared Function FixCodeFormat(Code As String) As String
        Code = Code.Between("【", "】").Between("[", "]") '从完整消息中提取
        Code = Code.Upper.Replace("O", "0").Replace("I", "1") '输入修正
        '版本 1 兼容
        If Code.Length >= 17 AndAlso (Code.Length < 23 OrElse (Code.Length >= 18 AndAlso Code(17) <> "-"c)) Then
            Code = Code.Substring(0, 17) & "-0105E"
        End If
        Return Code
    End Function

    '自动加入

    ''' <summary>
    ''' 切换到联机页并立即加入指定房间。
    ''' </summary>
    Public Shared Sub Join(Code As String)
        If LinkState <> LinkStates.Waiting Then
            Hint("你已经在联机房间中了！", HintType.Red)
        ElseIf FrmMain.PageCurrent = FormMain.PageType.Link Then
            FrmLinkMain.JoinInternal(Code)
        Else
            AutoJoinCode = Code
            FrmMain.PageChange(FormMain.PageType.Link, FormMain.PageSubType.LinkMain)
        End If
    End Sub
    '在进入页面时自动尝试加入房间
    Private Shared AutoJoinCode As String = Nothing
    Private Sub PanSelectJoin_Loaded() Handles PanSelectJoin.Loaded
        If AutoJoinCode Is Nothing Then Return
        If LinkState = LinkStates.Waiting Then JoinInternal(AutoJoinCode)
        AutoJoinCode = Nothing
    End Sub

#End Region

#Region "加载页面 (Loading | Failed)"

    '由 Waiting 或 Failed 状态切换到 Loading
    Private Sub SwitchToLoading(OldState As LinkStates)
        '清理变量
        Peers = Nothing
        FailCount = 0
        '加载器与进程状态
        LinkLoader.Start(IsForceRestart:=True)
        '页面切换会由 Loader 调用 MyPageRight 来触发
        'UI 更新
        UpdateProgressBar(0)
        LabLoadTitle.Text = If(IsServerSide, "创建房间中", "加入房间中")
        UpdateLoadingPage("正在初始化……", "准备初始化")
    End Sub

    '由 Loading 状态切换到 Failed
    Private Sub SwitchToFailed(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        ProcessStop()
        'UI 更新
        UpdateProgressBar(1)
        '显示错误信息
        LabLoadTitle.Text = FailReason
        Dim Brief As String = LinkLoader.Error.GetDisplay(False)
        Dim InnerEx As Exception = LinkLoader.Error
        If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Do Until InnerEx.InnerException Is Nothing
            InnerEx = InnerEx.InnerException
            If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Loop
        LabLoadDesc.Text = If(Brief.StartsWithF("$"), Brief.TrimStart("$"), LinkLoader.Error.GetDisplay(True))
        Logger.Warn(LinkLoader.Error, LabLoadTitle.Text)
    End Sub

    '===============================
    '  页面 UI
    '===============================

    Private Sub UpdateLoadingPage(Title As String, FailBrief As String)
        If FailReason = FailBrief & "失败" Then Return
        FailReason = FailBrief & "失败"
        Logger.Info($"开始步骤：{Title}")
        RunInUiWait(
        Sub()
            If FrmLinkMain Is Nothing OrElse Not FrmLinkMain.LabLoadDesc.IsLoaded Then Return
            FrmLinkMain.LabLoadDesc.Text = Title
        End Sub)
    End Sub
    Private FailReason As String = "准备初始化"

    '取消加载
    Private Sub BtnLoadCancel_Click() Handles BtnLoadCancel.Click
        ChangeState(LinkStates.Waiting)
    End Sub

    '点击重试
    Private Sub CardLoad_MouseLeftButtonUp() Handles CardLoad.MouseLeftButtonUp
        ChangeState(LinkStates.Loading)
    End Sub

    '进度条
    Private Sub UpdateProgressBar(Optional Value As Double = -1)
        If Value = -1 Then Value = LinkLoader.Progress
        Dim DisplayingProgress As Double = ColumnProgressA.Width.Value
        If Math.Round(Value - DisplayingProgress, 3) = 0 Then Return
        If DisplayingProgress <= Value Then
            Dim NewProgress As Double = If(Value = 1, 1, DisplayingProgress +
                (Value - DisplayingProgress) ^ 2 * 0.5)
            AniStart({
                AaGridLengthWidth(ColumnProgressA, NewProgress - ColumnProgressA.Width.Value, 200),
                AaGridLengthWidth(ColumnProgressB, (1 - NewProgress) - ColumnProgressB.Width.Value, 200)
            }, "Link Progress")
        Else
            ColumnProgressA.Width = New GridLength(Value, GridUnitType.Star)
            ColumnProgressB.Width = New GridLength(1 - Value, GridUnitType.Star)
            AniStop("Link Progress")
        End If
    End Sub
    Private Sub CardLoad_SizeChanged() Handles CardLoad.SizeChanged
        RectProgressClip.Rect = New Rect(0, 0, CardLoad.ActualWidth, 12)
    End Sub

#End Region

#Region "完成页面 (Finished)"

    '由 Loading 状态切换到 Finished
    Private Sub SwitchToFinished(OldState As LinkStates)
        '由于只可能从加载器的完成事件触发，不需要管加载器
        UpdatePanelVisibility() '页面的实际切换会由 Loader 调用 MyPageRight 来触发

        'UI 更新
        If IsServerSide Then
            LabFinishTitle.Text = "已创建房间"
            If LinkProviderNow = ModLink.LinkProvider.Terracotta Then
                LabFinishDesc.Text = $"把这个房间码发给朋友，他们用 PCL 加入后，在 Minecraft 的多人游戏里就能看到房间：{vbCrLf}{GetInviteCode()}"
            Else
                LabFinishDesc.Text = $"把这个地址发给朋友，让他们在 Minecraft 的多人游戏里添加服务器即可：{vbCrLf}{GetInviteCode()}"
            End If
            BtnFinishExit.Text = "关闭"
            BtnFinishPing.ToolTip = If(LinkProviderNow = ModLink.LinkProvider.Terracotta, "房间状态", "节点延迟")
            BtnFinishCopy.Visibility = Visibility.Visible
            Copy() '立即复制房间信息
            '下边栏
            BtnFinishPort.Visibility = Visibility.Collapsed
            LabFinishPort.Visibility = Visibility.Collapsed
            LabFinishIp.Text = GetInviteCode()
            BtnFinishIp.Visibility = Visibility.Visible
            LabFinishIp.Visibility = Visibility.Visible
        Else
            LabFinishTitle.Text = "已加入房间"
            LabFinishDesc.Text = "在 Minecraft 的多人游戏页面最下方就能找到联机房间！"
            BtnFinishExit.Text = "离开"
            BtnFinishPing.ToolTip = "连接状态"
            BtnFinishCopy.Visibility = Visibility.Collapsed
            BtnFinishPort.Visibility = Visibility.Collapsed
            LabFinishPort.Visibility = Visibility.Collapsed
            BtnFinishIp.Visibility = Visibility.Collapsed
            LabFinishIp.Visibility = Visibility.Collapsed
        End If
        Update() '立即刷新
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '退出
    Private Sub BtnFinishExit_Click() Handles BtnFinishExit.Click
        TryExit(False, False)
    End Sub
    ''' <summary>
    ''' 退出联机。
    ''' 返回是否弹出了警告窗口并且玩家选择了取消。
    ''' </summary>
    Public Function TryExit(Silent As Boolean, Closing As Boolean) As Boolean
        If LinkState = LinkStates.Waiting OrElse LinkState = LinkStates.Failed Then Return False
        If Not Silent Then
            If IsServerSide AndAlso PeopleCount > 1 Then
                If MyMsgBox("你确定要关闭联机房间吗？" & vbCrLf & "所有玩家都需要重新输入邀请码才可加入游戏！", "退出联机", "确定", "取消", IsWarn:=True) = 2 Then Return True
            ElseIf Closing Then
                If MyMsgBox(If(IsServerSide, "你确定要关闭联机房间吗？", "你确定要离开联机房间吗？"), "退出联机", "确定", "取消") = 2 Then Return True
            End If
        End If
        ChangeState(LinkStates.Waiting)
        Return False
    End Function

    '复制房间信息
    Private Sub Copy() Handles BtnFinishCopy.Click
        Dim Address As String = GetInviteCode()
        If String.IsNullOrEmpty(Address) Then Return
        Dim Text As String
        If LinkProviderNow = ModLink.LinkProvider.Terracotta Then
            Text = $"用 PCL Plus 的联机功能输入房间码【{Address}】即可加入房间！"
        Else
            Text = $"在 Minecraft 的多人游戏中添加服务器，地址填：{Address}"
        End If
        ClipboardSet(Text, False)
        Settings.Set("LinkLastAutoJoinInviteCode", Text)
        Hint("已复制房间信息！", HintType.Green)
    End Sub
    Private Function GetInviteCode() As String
        If LinkProviderNow = ModLink.LinkProvider.Terracotta Then Return If(TcRoomCode, "")
        Return If(LinkEndpoint, "")
    End Function
    '复制 IP
    Private Sub BtnFinishIp_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles BtnFinishIp.MouseLeftButtonUp
        ClipboardSet(ClientAddress, False)
        Hint("已复制服务器地址！", HintType.Green)
    End Sub

#End Region

    '===============================
    '  后端
    '===============================

    ''' <summary>
    ''' EasyTier 所在的文件夹路径，以 \ 结尾。
    ''' </summary>
    Private PathEasyTier As String = Paths.AppDataThenName & "EasyTier\"
    ''' <summary>
    ''' 当前是服务端还是客户端。
    ''' </summary>
    Private IsServerSide As Boolean
    ''' <summary>
    ''' 服务端映射前的端口号。
    ''' </summary>
    Private ServerPort As Integer
    ''' <summary>
    ''' 客户端映射后的端口号。
    ''' </summary>
    Private ClientPort As Integer
    ''' <summary>
    ''' RPC 端口号。
    ''' </summary>
    Private RPCPort As Integer
    ''' <summary>
    ''' 在客户端映射后的地址。
    ''' 除非已作为客户端启动 EasyTier，否则一直为 Nothing。
    ''' </summary>
    Private ClientAddress As String = Nothing
    ''' <summary>
    ''' 发现节点的 ID。
    ''' 若必须设定自定义节点则为 -2；若等待选择则为 -1；选择回退节点则为 0；否则为对应节点的 ID。
    ''' </summary>
    Private DiscoverNodeID As Integer = -1
    ''' <summary>
    ''' 网络信息。
    ''' </summary>
    Private NetworkName As String, NetworkSecret As String

#Region "加载"

    Private WithEvents LinkLoader As New LoaderCombo(Of Integer)("联机", {
        New LoaderTask(Of Integer, Integer)("获取配置", AddressOf InitConfig) With {.ProgressWeight = 8},
        New LoaderTask(Of Integer, Integer)("准备联机模块", AddressOf InitPrepareDownload) With {.ProgressWeight = 42},
        New LoaderTask(Of Integer, Integer)("启动联机模块", AddressOf InitLaunch) With {.ProgressWeight = 50}
    })

    '1. 检测端口（红石）/ 获取节点
    Private LinkNodes As List(Of ModLink.LinkNode)
    Private LinkJoinCode As String = Nothing
    Private LinkAutoDetectPort As Boolean = False
    Private Sub InitConfig(Task As LoaderTask(Of Integer, Integer))
        If LinkProviderNow = ModLink.LinkProvider.Hongshi Then
            If LinkAutoDetectPort Then
                UpdateLoadingPage("正在检测 Minecraft 的局域网端口……", "检测游戏端口")
                Dim Detected As Integer = LinkDetectMinecraftPort(8000)
                If Detected <= 0 Then
                    Throw New Exception("没有检测到 Minecraft 的局域网广播。" & vbCrLf &
                                        "请先在单人游戏里按 Esc 选择「对局域网开放」，然后再创建房间；" & vbCrLf &
                                        "或者改用「手动输入」填写聊天栏里的端口号。")
                End If
                ServerPort = Detected
                Logger.Info($"自动检测到 Minecraft 局域网端口：{Detected}")
            End If
            UpdateLoadingPage("正在获取联机节点……", "获取联机节点")
            LinkNodes = LinkFetchNodes()
            Dim Usable = LinkNodes.Where(Function(n) n.Reachable).ToList()
            Logger.Info($"可用联机节点 {Usable.Count} 个：{String.Join("，", Usable.Select(Function(n) n.ToString))}")
            If Not Usable.Any() Then Throw New Exception("所有联机节点都不可用，请检查网络连接后重试")
        Else
            UpdateLoadingPage("正在准备 Terracotta……", "准备 Terracotta")
        End If
        Task.Progress = 1
    End Sub
    '2. 准备客户端（同步单线程下载）
    '说明：不用 PCL 的多线程分段下载器。一是它在下载完成后偶发卡在 Loading 不推进，
    '二是 Gitee 不支持 HTTP Range，会被判定为下载源失效（Terracotta 正是因此失败）。
    Private Sub InitPrepareDownload(Task As LoaderTask(Of Integer, Integer))
        If LinkProviderNow = ModLink.LinkProvider.Hongshi Then
            UpdateLoadingPage("正在准备联机客户端……", "准备联机客户端")
            DirectoryUtils.Create(IO.Path.GetDirectoryName(LinkBinaryPath))
            If Not FileUtils.Exists(LinkBinaryPath) Then
                Logger.Info("联机客户端不存在，开始下载")
                Task.Progress = 0.1
                NetDownloadByClient(LinkClientApi, LinkBinaryPath)
                Logger.Info("联机客户端下载完成")
            End If
        Else
            UpdateLoadingPage("正在准备 Terracotta 客户端……", "准备 Terracotta")
            DirectoryUtils.Create(IO.Path.GetDirectoryName(TcArchivePath))
            If Not FileUtils.Exists(TcBinaryPath) Then
                Logger.Info("Terracotta 客户端不存在，开始下载")
                Task.Progress = 0.1
                NetDownloadByClient(TcDownloadAddress, TcArchivePath)
                Logger.Info("Terracotta 客户端下载完成")
            End If
        End If
        Task.Progress = 1
    End Sub
    '3. 建立隧道 / 建房 / 加入
    Private Sub InitLaunch(Task As LoaderTask(Of Integer, Integer))
        If LinkProviderNow = ModLink.LinkProvider.Hongshi Then
            UpdateLoadingPage("正在建立联机隧道……", "建立隧道")
            Dim Usable = LinkNodes.Where(Function(n) n.Reachable).ToList()
            Dim LastError As Exception = Nothing
            For Each Node As ModLink.LinkNode In Usable
                If Task.IsCanceled Then Throw New OperationCanceledException
                Try
                    LinkStartTunnel(ServerPort, Node, Sub(Msg) UpdateLoadingPage(Msg, "建立隧道"))
                    Task.Progress = 1
                    Return
                Catch ex As Exception
                    LastError = ex
                    Logger.Warn(ex, $"使用节点 {Node.Name} 建立隧道失败，尝试下一个节点")
                End Try
            Next
            Throw New Exception("所有节点都无法建立联机隧道。" & vbCrLf & If(LastError IsNot Nothing, LastError.Message, ""))
        Else
            'Terracotta
            If FileUtils.Exists(TcArchivePath) Then
                UpdateLoadingPage("正在解压 Terracotta……", "解压 Terracotta")
                Dim Folder As String = IO.Path.GetDirectoryName(TcBinaryPath)
                TcExtractTarGz(TcArchivePath, Folder)
                FileUtils.Delete(TcArchivePath, False)
                If Not FileUtils.Exists(TcBinaryPath) Then
                    Dim Found = DirectoryUtils.EnumerateFiles(Folder, False, "terracotta-*.exe").FirstOrDefault()
                    If Found IsNot Nothing Then FileUtils.Move(Found, TcBinaryPath)
                End If
            End If
            If Not FileUtils.Exists(TcBinaryPath) Then Throw New Exception("Terracotta 客户端解压失败")
            Task.Progress = 0.2
            Dim PlayerName As String = If(String.IsNullOrWhiteSpace(McLoginName()), "PCL Player", McLoginName())
            If IsServerSide Then
                TcHost(PlayerName, Sub(Msg) UpdateLoadingPage(Msg, "创建房间"))
            Else
                TcJoin(LinkJoinCode, PlayerName, Sub(Msg) UpdateLoadingPage(Msg, "加入房间"))
            End If
            Task.Progress = 1
        End If
    End Sub
    ''' <summary>
    ''' 从在线配置和 API 获取需要连接的节点列表，并更新发现节点 ID。
    ''' 根据发现节点 ID，会有以下行为变化：
    '''   -1：作为房主，根据负载均衡选择一个发现节点。
    '''   0：作为加入者，但邀请码未提供发现节点 ID，使用回退发现节点。
    '''   >0：作为加入者，根据 ID 选择对应的发现节点；如果没有，使用回退发现节点。
    ''' </summary>
    Private Function GetTargetPeers() As List(Of String)
        Dim FinalPeers As New List(Of String), FinalDiscoverID As Integer = -1
        Dim FallbackDiscoverID = ServerConfig("Link")("DiscoverPeerId").ToObject(Of Integer)
        Dim FallbackDiscoverAddress As String = ServerConfig("Link")("DiscoverPeer").ToString()
        Try
            '从 API 获取节点列表
            Dim RawNodes As JObject
            Dim IsFallbackRawList As Boolean = False
            Try
                RawNodes = NetRequestByClient("https://uptime.easytier.cn/api/nodes?page=1&per_page=1000", RequireJson:=True).DeserializeJson()
            Catch exx As Exception
                Logger.Warn(exx, "从源站获取节点列表失败，将使用 CDN 缓存")
                RawNodes = NetRequestByClientRetry("https://easytier.meloong.com/?page=1&per_page=1000", RequireJson:=True).DeserializeJson()
                IsFallbackRawList = True
            End Try
            '分析节点列表
            Dim Nodes As New List(Of JObject) '负载会添加在 load 字段上
            Dim BlackList As List(Of String) = ServerConfig("Link")("PeersBlackList").Select(Function(p) p.ToString).ToList()
            For Each Node As JObject In RawNodes("data")("items")
                '状态检查
                Dim ID = Node("id").ToObject(Of Integer)
                If Not Node("is_active").ToObject(Of Boolean) OrElse Not Node("is_approved").ToObject(Of Boolean) Then Continue For
                If ID = DiscoverNodeID Then GoTo ForcedPass '若为指定的发现节点，忽略后续检查
                If ID = FallbackDiscoverID Then Continue For '不主动选取回退发现节点
                If BlackList.Contains(Node("address").ToString) Then Continue For 'ServerConfig 黑名单
                If Node("usage_percentage").ToObject(Of Double) = 0 AndAlso RandomInteger(1, 100) <> 1 Then Continue For '或许节点有问题才导致是 0 负载，让它只有 1% 概率被选中
                '标签检查
                Dim Tags = Node("tags").Select(Function(t) t.ToString).ToList
                If Not Tags.Contains("国内") OrElse Not Tags.Contains("MC中继") Then Continue For
                '计算负载并加入列表
ForcedPass:
                Dim Load As Double = Node("usage_percentage").ToObject(Of Double) '负载
                Load *= 110 - Node("health_percentage_24h").ToObject(Of Double) '可用率
                Node("load") = Load
                Nodes.Add(Node)
            Next
            '排序
            If Not IsFallbackRawList Then
                Nodes = Nodes.OrderBy(Function(n) n("load").ToObject(Of Double)).ToList() '按负载从低到高排序
            Else
                Nodes = Nodes.Shuffle().ToList() '回退到 CDN 缓存时，由于负载数据可能过期，直接随机选择
            End If
            '选取发现节点
            Dim SelectedDiscoverNode As JObject = Nothing
            If DiscoverNodeID = -1 Then '-1：作为房主，根据负载均衡选择一个发现节点
                SelectedDiscoverNode = Nodes.FirstOrDefault(Function(n) Not n("allow_relay").ToObject(Of Boolean))
            ElseIf DiscoverNodeID > 0 Then '>0：作为加入者，根据 ID 选择对应的发现节点；如果没有，使用回退发现节点
                SelectedDiscoverNode = Nodes.FirstOrDefault(Function(n) n("id").ToObject(Of Integer) = DiscoverNodeID)
                If SelectedDiscoverNode Is Nothing Then
                    Logger.Warn($"未找到 ID {DiscoverNodeID} 的发现节点")
                    Panic("房间已过期", "请让房主重新创建房间！")
                    Throw New OperationCanceledException
                End If
            End If
            If SelectedDiscoverNode Is Nothing Then '使用回退发现节点
                SelectedDiscoverNode = New JObject From {{"address", FallbackDiscoverAddress}, {"id", FallbackDiscoverID}}
                Logger.Warn("将使用回退发现节点")
            End If
            FinalPeers.Add(SelectedDiscoverNode("address").ToString())
            FinalDiscoverID = SelectedDiscoverNode("id").ToObject(Of Integer)
            Logger.Info($"发现节点：{SelectedDiscoverNode("address")} (ID: {FinalDiscoverID})")
            '选取中继节点
            If ModeDebug OrElse Not IsServerSide Then '房主只连接发现节点，不连接中继节点
                Dim RelayCount As Integer = ServerConfig("Link")("RandomPeer").ToObject(Of Integer)
                Dim RelayNodes = Nodes.Where(Function(n) n("allow_relay").ToObject(Of Boolean)).ToList()
                If RelayNodes.Count < RelayCount Then Throw New Exception($"可用的中继节点数量不足，需要 {RelayCount} 个，实际 {RelayNodes.Count} 个")
                FinalPeers.AddRange(RelayNodes.Take(RelayCount).Select(Function(n) n("address").ToString()))
            End If
        Catch ex As Exception
            ex.ThrowIfCanceled()
            Logger.Error(ex, "获取节点列表失败，联机质量可能受到影响", LogBehavior.Toast)
            FinalPeers.AddRange(ServerConfig("Link")("Peers").Select(Function(p) p.ToString))
            If FinalDiscoverID <= 0 Then
                FinalPeers.Add(FallbackDiscoverAddress)
                FinalDiscoverID = FallbackDiscoverID
            End If
        End Try
        '版本 1 兼容
        If CType(ServerConfig("Link"), JObject).ContainsKey("EnableForcedPeer") Then
            FinalPeers.Add("tcp://mc1.easytier.cn:55558")
            If FinalDiscoverID <= 0 Then FinalDiscoverID = 94
        End If
        '强制添加的节点
        FinalPeers.AddRange(ServerConfig("Link")("MandatoryPeers").Select(Function(p) p.ToString))
        '结束
        DiscoverNodeID = FinalDiscoverID
        Return FinalPeers.Distinct.ToList
    End Function

#End Region

#Region "进程与日志"

    ''' <summary>
    ''' EasyTier Core 进程。
    ''' </summary>
    Private Shared ProcessCore As Process
    Private Shared ProcessOutputHandle As AutoResetEvent, ProcessErrorHandle As AutoResetEvent

    ''' <summary>
    ''' 若程序正在运行，则结束程序进程。
    ''' 即使失败也不会抛出异常。
    ''' </summary>
    Public Shared Sub ProcessStop()
        LinkStopTunnel()
        TcStop()
        Try
            '关闭未捕获的进程
            For Each ProcessObject In Process.GetProcesses
                If ProcessObject.Id = ProcessCore?.Id Then Continue For
                If ProcessObject.ProcessName <> "联机模块" AndAlso ProcessObject.ProcessName <> "联机模块 CLI" Then Continue For
                Try
                    Logger.Info($"停止残留的联机模块，PID：{ProcessObject.Id}，进程名：{ProcessObject.ProcessName}")
                    ProcessObject.Kill()
                    ProcessObject.Close()
                Catch exx As Exception
                    Logger.Warn(exx, $"结束进程失败（{ProcessObject.ProcessName}，PID {ProcessObject.Id}）")
                End Try
            Next
            '关闭由自身启动的进程
            If ProcessCore IsNot Nothing AndAlso Not ProcessCore.HasExited Then
                Logger.Info($"停止所启动的联机模块，PID：{ProcessCore.Id}")
                ProcessCore.Kill()
                ProcessCore.Close()
                ProcessCore = Nothing
            End If
            '释放资源
            ProcessOutputHandle?.Dispose()
            ProcessErrorHandle?.Dispose()
        Catch ex As Exception
            Logger.Warn(ex, "停止联机模块失败")
        End Try
    End Sub
    ''' <summary>
    ''' 出现意外错误，给出错误信息并结束联机。
    ''' </summary>
    Private Sub Panic(Brief As String, Detail As String)
        '常见原因分析
        If Detail.Contains("failed to listen on ") Then
            Detail = $"监听端口失败。{vbCrLf}请点击重试，如果还是出现此错误，可以重启电脑解决。{vbCrLf}{vbCrLf}{Detail}"
        End If
        '显示信息
        If LinkState = LinkStates.Loading Then
            FailReason = Brief
            LinkLoader.Failed(New Exception(Detail))
        Else
            MyMsgBox(Detail, "联机出错：" & Brief, IsWarn:=True)
            ChangeState(LinkStates.Waiting)
            Telemetry("联机失败", "Exception", FilterUserName(Brief & "：" & Detail, "*"))
        End If
    End Sub

    ''' <summary>
    ''' 启动进程。
    ''' </summary>
    Private Sub ProcessStart(Arguments As String)
        Dim Info = New ProcessStartInfo With {
            .FileName = PathEasyTier & "联机模块.exe",
            .Arguments = Arguments,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardError = True,
            .RedirectStandardOutput = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Logger.Info($"正在启动 EasyTier：{Arguments}")
        ProcessCore = New Process With {.StartInfo = Info}
        Dim LogLineHandler =
        Sub(sender As Object, e As DataReceivedEventArgs, Handle As AutoResetEvent)
            Try
                If e.Data Is Nothing Then
                    Handle.[Set]()
                    Return
                End If
                ProcessLogLine(e.Data)
            Catch unused As ObjectDisposedException
            Catch ex As Exception
                Logger.Warn(ex, "读取 EasyTier 信息失败")
            End Try
        End Sub
        ProcessOutputHandle = New AutoResetEvent(False)
        AddHandler ProcessCore.OutputDataReceived, Sub(sender, e) LogLineHandler(sender, e, ProcessOutputHandle)
        ProcessErrorHandle = New AutoResetEvent(False)
        AddHandler ProcessCore.ErrorDataReceived, Sub(sender, e) LogLineHandler(sender, e, ProcessErrorHandle)
        ProcessCore.Start()
        ProcessCore.BeginOutputReadLine()
        ProcessCore.BeginErrorReadLine()
    End Sub
    Private Sub ProcessLogLine(Line As String)
        '记录日志
        Logger.Info(Line)
        LogHistory.Enqueue(Line)
        If LogHistory.Count >= 10 Then LogHistory.Dequeue()
        '检查日志内容
        If Line.ContainsIgnoreCase("new peer connection added") Then
            Logger.Warn($"已建立连接：{If(Line.RegexSeek("(?<=remote_addr.+?"")[^""}]{3,}"), Line)}")
            Update()
        ElseIf Line.ContainsIgnoreCase("peer connection removed") Then
            Logger.Warn($"已断开连接：{If(Line.RegexSeek("(?<=remote_addr.+?"")[^""}]{3,}"), Line)}")
            Update()
        End If
    End Sub
    Private LogHistory As New Queue(Of String)(10)

#End Region

#Region "CLI 节点信息获取"

    Private Class Peer
        ''' <summary>
        ''' 该节点的类型。
        ''' </summary>
        Public ReadOnly Type As Types
        Public Enum Types
            ''' <summary>
            ''' 客户端。
            ''' </summary>
            Client
            ''' <summary>
            ''' 服务器。
            ''' </summary>
            Server
            ''' <summary>
            ''' 公共节点。
            ''' </summary>
            Misc
            ''' <summary>
            ''' 自己，或者自己的重复（它可能返回多个自己）。
            ''' </summary>
            Self
        End Enum
        ''' <summary>
        ''' 与该节点的延迟 (ms)。
        ''' 若是自己则为 0。
        ''' </summary>
        Public ReadOnly Ping As Double
        ''' <summary>
        ''' 名称。
        ''' </summary>
        Public ReadOnly Name As String
        ''' <summary>
        ''' 是否通过中继连接。
        ''' </summary>
        Public ReadOnly Relay As Boolean
        ''' <summary>
        ''' NAT 类型。
        ''' </summary>
        Public ReadOnly NATType As NATTypes

        ''' <summary>
        ''' 从 CLI 给出的信息分析对应的数据。
        ''' </summary>
        Public Sub New(Info As JObject)
            '类别
            Dim PeerName = Info("hostname").ToString
            If Info("cost").ToString = "Local" Then
                Type = Types.Self
            ElseIf PeerName.StartsWithF("Client") Then
                Type = Types.Client
            ElseIf Info("ipv4") = "10.114.114.114" Then '只允许这个 IP 作为房主，避免网络被多个房主搞炸的情况
                Type = Types.Server
            Else
                Type = Types.Misc
            End If
            '基础信息
            Double.TryParse(Info("lat_ms"), NumberStyles.Any, CultureInfo.InvariantCulture, Ping)
            Name = PeerName
            Relay = Info("cost").ToString.ContainsIgnoreCase("relay")
            NATType = EnumUtils.FromString(Of NATTypes)(Info("nat_type"))
        End Sub
        Public Overrides Function ToString() As String
            Return $"{Type} - {Name} - Ping {Ping:0.0}ms [中继? {Relay}] - NAT {NATType}"
        End Function
    End Class
    Private Enum NATTypes
        'https://github.com/EasyTier/EasyTier/blob/6bb2fd9a15ab2499bdeabdcc3a925e9bd9aebf50/easytier/src/proto/common.proto#L129
        Unknown = 0
        OpenInternet = 1
        NoPAT = 2
        FullCone = 3
        Restricted = 4
        PortRestricted = 5
        Symmetric = 6
        SymUdpFirewall = 7
        SymmetricEasyInc = 8
        SymmetricEasyDec = 9
        ''' <summary>
        ''' 尚未获取。
        ''' </summary>
        Pending = 10
    End Enum

    ''' <summary>
    ''' 当前的节点列表，使用 RefreshPeerLoader 来刷新。
    ''' 若尚未成功获取过则为 Nothing，但保证在加载完成后至少是一个列表。
    ''' </summary>
    Private Peers As List(Of Peer) = Nothing
    ''' <summary>
    ''' 自己的 NAT 类型。
    ''' </summary>
    Private NATType As NATTypes = NATTypes.Pending

    ''' <summary>
    ''' 调用 EasyTier CLI 获取已连接节点信息。
    ''' 这会过滤掉尚未完成连接的其他节点以及自己。
    ''' 若在加载完成后连续获取失败 3 次，则会强制断开连接。
    ''' </summary>
    Private RefreshPeerLoader As New LoaderTask(Of Integer, List(Of Peer))("EasyTier CLI", AddressOf RefreshPeer)
    Private Sub RefreshPeer()
        Try
            Dim CliResult = "" 'StartProcessAndGetOutput(PathEasyTier & "联机模块 CLI.exe", $"-o json -p 127.0.0.1:{RPCPort} peer", 2000, Encoding:=Encoding.UTF8, PrintLog:=False)
            '解析
            If Not CliResult.Contains("lat_ms") Then Throw New Exception("CLI 调用失败：" & vbCrLf & CliResult)
            If GetUuid() Mod If(ModeDebug, 23, 103) = 0 Then Logger.Info($"CLI 输出抽样：{vbCrLf}{CliResult}")
            Dim NewPeers As New List(Of Peer)
            For Each Line As JObject In CType(CliResult.DeserializeJson(), JArray)
                Try
                    Dim Peer = New Peer(Line)
                    If Peer.Type = Peer.Types.Self Then
                        NATType = Peer.NATType '记录自己的 NAT
                    Else
                        NewPeers.Add(Peer)
                    End If
                Catch exx As Exception
                    Logger.Warn(exx, $"错误的信息（{Line}）")
                End Try
            Next
            '完成
            Peers = NewPeers
            FailCount = 0
        Catch ex As Exception
            Logger.Warn(ex, "获取节点信息失败")
            If LinkState = LinkStates.Finished Then
                FailCount += 1
                If FailCount >= 4 Then Panic("获取节点信息失败", ex.Message)
            End If
        End Try
    End Sub
    ''' <summary>
    ''' CLI 调用连续失败的次数。
    ''' </summary>
    Private FailCount As Integer = 0

    ''' <summary>
    ''' 根据当前的节点列表获取 Ping 值。
    ''' 服务端会返回所有节点中最低的 Ping，客户端会返回服务端的 Ping。
    ''' 0 代表尚不可用。
    ''' </summary>
    Private Function GetPeerPing() As Double
        Dim Peer = GetTargetPeer()
        If Peer Is Nothing Then Return 0
        Return Peer.Ping
    End Function
    ''' <summary>
    ''' 服务端会返回所有节点中 Ping 大于 0 最低的那一个，客户端会返回服务端。
    ''' 若没有则为 Nothing。
    ''' </summary>
    Private Function GetTargetPeer() As Peer
        If Peers Is Nothing Then Return Nothing
        Dim Targets As IEnumerable(Of Peer)
        If IsServerSide Then
            Targets = Peers.Where(Function(p) p.Ping > 0)
        Else
            Targets = Peers.Where(Function(p) p.Type = Peer.Types.Server AndAlso p.Ping > 0)
        End If
        If Not Targets.Any Then Return Nothing
        '返回 Ping 且最低的那个
        Dim MinPing = Targets.Min(Function(p) p.Ping)
        Return Targets.First(Function(p) p.Ping = MinPing)
    End Function

    ''' <summary>
    ''' 获取当前房间中的人数，包括自己。
    ''' 0 代表不可用。
    ''' </summary>
    Private ReadOnly Property PeopleCount As Integer
        Get
            If Peers Is Nothing Then Return 0
            Return Peers.Where(Function(p) p.Type <> Peer.Types.Misc).Count() + 1 '加上自己
        End Get
    End Property

#End Region

#Region "定时任务"

    '启动
    Private Sub StartTimerThread() Handles Me.Loaded
        Static IsTimerStarted As Boolean = False
        If IsTimerStarted Then Return
        RunInNewThread(
        Sub()
            Dim Counter As Integer = 0
            Do While True
                Try
                    Thread.Sleep(200)
                    Counter += 200
                    '每秒执行一次
                    If Counter >= 1000 Then
                        Counter = 0
                        Update()
                    End If
                    '每 200ms 更新进度条
                    If LinkState = LinkStates.Loading Then RunInUi(AddressOf UpdateProgressBar)
                Catch ex As Exception
                    Logger.Error(ex, "联机模块主时钟出错")
                    Thread.Sleep(10000)
                End Try
            Loop
        End Sub, "Link Timer")
    End Sub

    '每秒或进入页面时触发
    Private Sub Update() Handles Me.Loaded
        If LinkState <> LinkStates.Finished Then Return
        If FrmMain.PageCurrent <> FormMain.PageType.Link Then Return
        RunInUi(
        Sub()
            'Logo 旋转动画
            AniStart(AaRotateTransform(ImgFinishLogo, 500, 5000), "Link Logo Rotation")
            HintFinish.Visibility = Visibility.Collapsed
            LabFinishPlayer.Text = "1  人"
            If LinkProviderNow = ModLink.LinkProvider.Terracotta Then
                'Terracotta：只关心客户端进程是否还在
                LabFinishPing.Text = If(TcRunning, "已连接", "未连接")
                If Not TcRunning Then
                    HintFinish.Visibility = Visibility.Visible
                    HintFinish.Theme = MyHint.Themes.Yellow
                    HintFinish.Text = "联机模块已断开，请退出后重新创建房间。"
                End If
            Else
                '红石：显示所连节点的延迟，并检测隧道是否掉线
                Dim Node = LinkCurrentNode
                LabFinishPing.Text = If(Node IsNot Nothing AndAlso Node.Reachable, $"{Node.Latency} ms", "已连接")
                If Not LinkRunning Then
                    HintFinish.Visibility = Visibility.Visible
                    HintFinish.Theme = MyHint.Themes.Yellow
                    HintFinish.Text = "联机隧道已断开，请退出后重新创建房间。"
                End If
            End If
        End Sub)
    End Sub
#End Region

End Class
