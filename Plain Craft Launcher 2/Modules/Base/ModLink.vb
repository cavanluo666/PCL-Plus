Imports System.Diagnostics
Imports System.Net.Sockets
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks

''' <summary>
''' 红石联机（RedStone）模块。
''' 通过 hongshi.site 的社区中继节点，把本地 Minecraft 局域网端口映射到公网，
''' 其他玩家无需安装任何客户端，直接在游戏的多人游戏里连接该地址即可。
''' </summary>
Public Module ModLink

    ''' <summary>可选的联机方式。</summary>
    Public Enum LinkProvider
        ''' <summary>红石：端口转发，加入方无需安装任何软件。</summary>
        Hongshi = 0
        ''' <summary>Terracotta：EasyTier 虚拟局域网，用房间码加入。</summary>
        Terracotta = 1
    End Enum

    ''' <summary>当前选中的联机方式。</summary>
    Public LinkProviderNow As LinkProvider = LinkProvider.Hongshi

    ''' <summary>联机服务的基础地址。</summary>
    Public Const LinkApiBase As String = "https://hongshi.site"
    ''' <summary>节点列表接口。</summary>
    Private Const LinkNodeApi As String = LinkApiBase & "/api/server/list"
    ''' <summary>客户端下载接口。</summary>
    Public Const LinkClientApi As String = LinkApiBase & "/api/download/client?platform=windows&arch=amd64"
    ''' <summary>节点探测使用的中继端口。</summary>
    Private Const LinkProbePort As Integer = 8080
    ''' <summary>节点探测超时（毫秒）。</summary>
    Private Const LinkProbeTimeout As Integer = 1500

    ''' <summary>一个可用的社区中继节点。</summary>
    Public Class LinkNode
        Public Name As String
        Public Address As String
        ''' <summary>探测到的延迟（毫秒）；-1 表示不可达。</summary>
        Public Latency As Integer = -1
        Public ReadOnly Property Reachable As Boolean
            Get
                Return Latency >= 0
            End Get
        End Property
        Public Overrides Function ToString() As String
            Return $"{Name}（{Address}，{If(Reachable, Latency & " ms", "不可达")}）"
        End Function
    End Class

    ''' <summary>联机客户端可执行文件的路径。</summary>
    Public ReadOnly Property LinkBinaryPath As String
        Get
            Return Paths.Base & "PCL\Link\redstone.exe"
        End Get
    End Property

    Private ReadOnly LinkLogQueue As New ConcurrentQueue(Of String)
    Private LinkProcess As Process

    ''' <summary>当前隧道的公网地址，形如 nj.hongshi.site:31310；未建立时为 Nothing。</summary>
    Public LinkEndpoint As String = Nothing
    ''' <summary>隧道映射的本地端口。</summary>
    Public LinkLocalPort As Integer = 0
    ''' <summary>隧道当前使用的节点。</summary>
    Public LinkCurrentNode As LinkNode = Nothing
    '红石协议的 control / data 端口是连接中继节点的约定端口，不能动态分配。
    '实测：换成随机高位端口会导致客户端去连节点的该端口并被拒绝（WSAECONNREFUSED 10061）。
    Private Const LinkControlPort As Integer = 8080
    Private Const LinkDataPort As Integer = 8000

    ''' <summary>隧道进程最近的输出，出错时用于排查。</summary>
    Public ReadOnly Property LinkLogText As String
        Get
            Return String.Join(vbCrLf, LinkLogQueue)
        End Get
    End Property

    ''' <summary>隧道是否正在运行。</summary>
    Public ReadOnly Property LinkRunning As Boolean
        Get
            Return LinkProcess IsNot Nothing AndAlso Not LinkProcess.HasExited
        End Get
    End Property

#Region "节点"

    ''' <summary>
    ''' 获取社区中继节点列表，并按延迟从低到高排序。
    ''' </summary>
    Public Function LinkFetchNodes() As List(Of LinkNode)
        Dim Result As New List(Of LinkNode)
        Dim Raw As JObject = NetRequestByClientRetry(LinkNodeApi, RequireJson:=True).DeserializeJson()
        For Each Item As JProperty In Raw.Properties
            Result.Add(New LinkNode With {.Name = Item.Name, .Address = Item.Value.ToString})
        Next
        If Not Result.Any() Then Throw New Exception("联机服务没有返回任何可用节点")
        '并发探测，避免串行等待
        Dim Tasks As Task() = Result.Select(Function(Node As LinkNode) Task.Run(Sub() Node.Latency = LinkProbe(Node.Address))).ToArray()
        Task.WaitAll(Tasks)
        Return Result.OrderBy(Function(Node As LinkNode) If(Node.Reachable, Node.Latency, Integer.MaxValue)).ToList()
    End Function

    ''' <summary>探测节点连通性，返回延迟毫秒数；不可达返回 -1。</summary>
    Private Function LinkProbe(Address As String) As Integer
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Dim Client As New TcpClient
        Try
            Dim Connecting = Client.ConnectAsync(Address, LinkProbePort)
            If Not Connecting.Wait(LinkProbeTimeout) OrElse Not Client.Connected Then Return -1
            Watch.Stop()
            Return CInt(Watch.ElapsedMilliseconds)
        Catch
            Return -1
        Finally
            Try : Client.Close() : Catch : End Try
        End Try
    End Function

#End Region

#Region "隧道"

    ''' <summary>
    ''' 启动隧道，把本地端口映射到公网，并等到拿到公网地址为止。
    ''' 必须在工作线程执行；失败时抛出异常。
    ''' </summary>
    Public Sub LinkStartTunnel(Port As Integer, Node As LinkNode, Optional OnProgress As Action(Of String) = Nothing)
        LinkStopTunnel()
        If Not FileUtils.Exists(LinkBinaryPath) Then Throw New Exception("联机客户端尚未下载完成")
        LinkLocalPort = Port
        LinkCurrentNode = Node
        LinkEndpoint = Nothing
        Dim Discard As String = Nothing
        While LinkLogQueue.TryDequeue(Discard)
        End While
        If OnProgress IsNot Nothing Then OnProgress($"正在连接节点 {Node.Name}……")
        Dim Folder As String = IO.Path.GetDirectoryName(LinkBinaryPath)
        DirectoryUtils.Create(Folder)
        Dim Info As New ProcessStartInfo(LinkBinaryPath) With {
            .Arguments = $"-t {Node.Address} -p {Port} --game-host 127.0.0.1 --control-port {LinkControlPort} --data-port {LinkDataPort}",
            .WorkingDirectory = Folder,
            .UseShellExecute = False,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .CreateNoWindow = True}
        Info.EnvironmentVariables("RUST_LOG") = "info"
        Info.EnvironmentVariables("NO_COLOR") = "1"
        LinkProcess = Process.Start(Info)
        If LinkProcess Is Nothing Then Throw New Exception("无法启动联机客户端")
        AddHandler LinkProcess.OutputDataReceived, AddressOf LinkProcessOutput
        AddHandler LinkProcess.ErrorDataReceived, AddressOf LinkProcessOutput
        LinkProcess.BeginOutputReadLine()
        LinkProcess.BeginErrorReadLine()
        '等待公网地址
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Do
            Thread.Sleep(200)
            If LinkEndpoint IsNot Nothing Then
                Logger.Info($"联机隧道已建立：{LinkEndpoint}")
                Return
            End If
            If LinkProcess.HasExited Then
                Throw New Exception($"联机客户端意外退出（代码 {LinkProcess.ExitCode}）。{vbCrLf}{LinkLogText}")
            End If
            If Watch.ElapsedMilliseconds > 30000 Then
                Throw New Exception($"连接节点 {Node.Name} 超时，请稍后重试。{vbCrLf}{LinkLogText}")
            End If
        Loop
    End Sub

    Private Sub LinkProcessOutput(sender As Object, e As DataReceivedEventArgs)
        If String.IsNullOrEmpty(e.Data) Then Return
        Logger.Info($"[联机] {e.Data}")
        LinkLogQueue.Enqueue(e.Data)
        While LinkLogQueue.Count > 30
            Dim Discard As String = Nothing
            LinkLogQueue.TryDequeue(Discard)
        End While
        If LinkEndpoint Is Nothing Then
            Dim Matched = Regex.Match(e.Data, "endpoint=([^\s]+)")
            If Matched.Success Then LinkEndpoint = Matched.Groups(1).Value
        End If
    End Sub

    ''' <summary>停止隧道。即使失败也不会抛出异常。</summary>
    Public Sub LinkStopTunnel()
        LinkEndpoint = Nothing
        LinkCurrentNode = Nothing
        If LinkProcess Is Nothing Then Return
        Try
            If Not LinkProcess.HasExited Then
                LinkProcess.Kill()
                LinkProcess.WaitForExit(3000)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "结束联机客户端失败")
        Finally
            Try : LinkProcess.Dispose() : Catch : End Try
            LinkProcess = Nothing
        End Try
    End Sub

#End Region

#Region "Minecraft 局域网端口检测"

    ''' <summary>
    ''' 监听 Minecraft 的局域网广播（224.0.2.60:4445），返回游戏当前开放的端口。
    ''' 超时或未检测到返回 0。
    ''' </summary>
    Public Function LinkDetectMinecraftPort(TimeoutMs As Integer) As Integer
        Dim Found As Integer = 0
        Try
            Using Client As New UdpClient()
                Client.ExclusiveAddressUse = False
                Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, True)
                Client.Client.Bind(New IPEndPoint(IPAddress.Any, 4445))
                Client.JoinMulticastGroup(IPAddress.Parse("224.0.2.60"))
                Dim Watch As Stopwatch = Stopwatch.StartNew()
                Do While Watch.ElapsedMilliseconds < TimeoutMs
                    If Client.Available > 0 Then
                        Dim Remote As New IPEndPoint(IPAddress.Any, 0)
                        Dim Data As Byte() = Client.Receive(Remote)
                        Dim Message As String = Text.Encoding.UTF8.GetString(Data)
                        Dim Matched = Regex.Match(Message, "\[AD\](\d{1,5})\[/AD\]")
                        If Matched.Success Then
                            Found = CInt(Matched.Groups(1).Value)
                            Logger.Info($"检测到 Minecraft 局域网广播，端口 {Found}")
                            Exit Do
                        End If
                    Else
                        Thread.Sleep(100)
                    End If
                Loop
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "检测 Minecraft 局域网端口失败")
        End Try
        Return Found
    End Function

#End Region

#Region "Terracotta 联机（虚拟局域网）"

    ''' <summary>Terracotta 客户端版本。</summary>
    Public Const TcVersion As String = "0.4.2"
    ''' <summary>Terracotta 使用的中继节点。</summary>
    Public Const TcPublicNode As String = "wss://center.node.1tmc.top"

    ''' <summary>客户端压缩包下载地址（gitee 镜像，国内可达）。</summary>
    Public ReadOnly Property TcDownloadAddress As String
        Get
            Return $"https://gitee.com/burningtnt/Terracotta/releases/download/v{TcVersion}/terracotta-{TcVersion}-windows-x86_64-pkg.tar.gz"
        End Get
    End Property
    ''' <summary>客户端压缩包路径。</summary>
    Public ReadOnly Property TcArchivePath As String
        Get
            Return Paths.Base & "PCL\Link\terracotta.tar.gz"
        End Get
    End Property
    ''' <summary>客户端可执行文件路径。</summary>
    Public ReadOnly Property TcBinaryPath As String
        Get
            Return Paths.Base & "PCL\Link\terracotta.exe"
        End Get
    End Property
    ''' <summary>客户端是否已就绪。</summary>
    Public ReadOnly Property TcInstalled As Boolean
        Get
            Return FileUtils.Exists(TcBinaryPath)
        End Get
    End Property

    ''' <summary>当前房间码，形如 U/98XV-E6PK-MTND-CWZ5；未建房时为 Nothing。</summary>
    Public TcRoomCode As String = Nothing

    Private TcProcess As Process
    Private TcHttpPort As Integer = 0
    Private ReadOnly TcLogQueue As New ConcurrentQueue(Of String)
    Private Const TcMaxWait As Integer = 90000

    Public ReadOnly Property TcLogText As String
        Get
            Return String.Join(vbCrLf, TcLogQueue)
        End Get
    End Property
    Public ReadOnly Property TcRunning As Boolean
        Get
            Return TcProcess IsNot Nothing AndAlso Not TcProcess.HasExited
        End Get
    End Property

    ''' <summary>
    ''' 解压 tar.gz。.NET Framework 没有内置 tar 支持，这里手工解析 ustar 格式。
    ''' </summary>
    Public Sub TcExtractTarGz(GzPath As String, DestDir As String)
        DirectoryUtils.Create(DestDir)
        Using Fs As New IO.FileStream(GzPath, IO.FileMode.Open, IO.FileAccess.Read)
            Using Gz As New IO.Compression.GZipStream(Fs, IO.Compression.CompressionMode.Decompress)
                Dim Header(511) As Byte
                Do
                    If Not LinkReadExactly(Gz, Header, 512) Then Exit Do
                    Dim AllZero As Boolean = True
                    For i = 0 To 511
                        If Header(i) <> 0 Then AllZero = False : Exit For
                    Next
                    If AllZero Then Exit Do '两个全零块代表归档结束
                    Dim Name As String = Text.Encoding.UTF8.GetString(Header, 0, 100).TrimEnd(ChrW(0))
                    Dim SizeField As String = Text.Encoding.ASCII.GetString(Header, 124, 12).TrimEnd(ChrW(0), " "c)
                    Dim Size As Long = 0
                    If SizeField.Length > 0 Then Size = Convert.ToInt64(SizeField, 8)
                    Dim TypeFlag As Byte = Header(156)
                    Dim Pad As Integer = CInt((512 - (Size Mod 512)) Mod 512)
                    If TypeFlag = 0 OrElse TypeFlag = 48 Then '普通文件
                        If Size > 0 Then
                            Dim Data(CInt(Size) - 1) As Byte
                            If Not LinkReadExactly(Gz, Data, Data.Length) Then Exit Do
                            Dim Target As String = IO.Path.Combine(DestDir, Name.Replace("/", "\"))
                            DirectoryUtils.Create(IO.Path.GetDirectoryName(Target))
                            IO.File.WriteAllBytes(Target, Data)
                        End If
                        If Pad > 0 Then
                            Dim Skip(Pad - 1) As Byte
                            LinkReadExactly(Gz, Skip, Pad)
                        End If
                    ElseIf TypeFlag = 53 Then
                        '目录，无需处理
                    Else
                        '其他类型，跳过其数据
                        If Size > 0 Then
                            Dim Skip(CInt(Size) + Pad - 1) As Byte
                            LinkReadExactly(Gz, Skip, Skip.Length)
                        End If
                    End If
                Loop
            End Using
        End Using
        Logger.Info($"已解压 {IO.Path.GetFileName(GzPath)} 到 {DestDir}")
    End Sub

    Private Function LinkReadExactly(Source As IO.Stream, Buffer As Byte(), Count As Integer) As Boolean
        Dim Offset As Integer = 0
        While Offset < Count
            Dim Got As Integer = Source.Read(Buffer, Offset, Count - Offset)
            If Got <= 0 Then Return False
            Offset += Got
        End While
        Return True
    End Function

    ''' <summary>启动 Terracotta 客户端，并等到本地 HTTP 控制端口就绪。</summary>
    Private Sub TcStartClient()
        If TcRunning Then Return
        If Not FileUtils.Exists(TcBinaryPath) Then Throw New Exception("Terracotta 客户端尚未安装")
        Dim Folder As String = IO.Path.GetDirectoryName(TcBinaryPath)
        DirectoryUtils.Create(Folder)
        Dim PortFile As String = IO.Path.Combine(Folder, "tc-port.json")
        If FileUtils.Exists(PortFile) Then FileUtils.Delete(PortFile, False)
        Dim Info As New ProcessStartInfo(TcBinaryPath) With {
            .Arguments = $"--hmcl ""{PortFile}""",
            .WorkingDirectory = Folder,
            .UseShellExecute = False,
            .CreateNoWindow = True}
        TcProcess = Process.Start(Info)
        If TcProcess Is Nothing Then Throw New Exception("无法启动 Terracotta 客户端")
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Do
            Thread.Sleep(250)
            If FileUtils.Exists(PortFile) Then
                Try
                    Dim Json As JObject = FileUtils.ReadAsString(PortFile).DeserializeJson()
                    Dim P As Integer = Json("port").ToObject(Of Integer)
                    If P > 0 Then
                        TcHttpPort = P
                        Logger.Info($"Terracotta 控制端口：{TcHttpPort}")
                        Return
                    End If
                Catch
                End Try
            End If
            If TcProcess.HasExited Then Throw New Exception($"Terracotta 客户端启动后立即退出（代码 {TcProcess.ExitCode}）。")
            If Watch.ElapsedMilliseconds > 25000 Then Throw New Exception("启动 Terracotta 客户端超时。")
        Loop
    End Sub

    ''' <summary>调用 Terracotta 的本地控制接口。</summary>
    Private Function TcRequest(Path As String) As String
        Return NetRequestByClient($"http://127.0.0.1:{TcHttpPort}{Path}", Timeout:=15000, MakeLog:=False)
    End Function

    Private Function TcGetState() As JObject
        Try
            Return TcRequest("/state").DeserializeJson()
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>读取 Terracotta 客户端日志。</summary>
    Public Function TcFetchLog() As String
        Try
            Return TcRequest("/log?fetch=")
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 创建房间。需要本地 Minecraft 已「对局域网开放」，Terracotta 会自动扫描到端口。
    ''' 返回房间码。
    ''' </summary>
    Public Function TcHost(PlayerName As String, Optional OnProgress As Action(Of String) = Nothing) As String
        TcRoomCode = Nothing
        If OnProgress IsNot Nothing Then OnProgress("正在启动 Terracotta 客户端……")
        TcStartClient()
        If OnProgress IsNot Nothing Then OnProgress("正在扫描本地 Minecraft 服务器……请确保已在游戏里「对局域网开放」")
        TcRequest($"/state/scanning?room=&player={Uri.EscapeDataString(PlayerName)}&public_nodes={Uri.EscapeDataString(TcPublicNode)}")
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Do
            Thread.Sleep(500)
            Dim State As JObject = TcGetState()
            If State IsNot Nothing Then
                Dim S As String = If(State("state")?.ToString, "")
                Select Case S
                    Case "host-starting"
                        If OnProgress IsNot Nothing Then OnProgress("正在建立虚拟局域网……")
                    Case "host-ok"
                        TcRoomCode = If(State("room")?.ToString, "")
                        Logger.Info($"Terracotta 房间已建立：{TcRoomCode}")
                        Return TcRoomCode
                    Case "exception", "fatal"
                        Throw New Exception("Terracotta 创建房间失败。" & vbCrLf & TcFetchLog())
                End Select
            End If
            If Watch.ElapsedMilliseconds > TcMaxWait Then
                Throw New Exception("等待 Terracotta 扫描到 Minecraft 服务器超时。" & vbCrLf &
                                    "请先在游戏里选择「对局域网开放」，等待几秒后重试。")
            End If
        Loop
    End Function

    ''' <summary>加入房间。成功后本地 Minecraft 的多人游戏里就能看到该房间。</summary>
    Public Sub TcJoin(RoomCode As String, PlayerName As String, Optional OnProgress As Action(Of String) = Nothing)
        If OnProgress IsNot Nothing Then OnProgress("正在启动 Terracotta 客户端……")
        TcStartClient()
        If OnProgress IsNot Nothing Then OnProgress("正在加入房间……")
        TcRequest($"/state/guesting?room={Uri.EscapeDataString(RoomCode)}&player={Uri.EscapeDataString(PlayerName)}&public_nodes={Uri.EscapeDataString(TcPublicNode)}")
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Do
            Thread.Sleep(500)
            Dim State As JObject = TcGetState()
            If State IsNot Nothing Then
                Dim S As String = If(State("state")?.ToString, "")
                Select Case S
                    Case "guest-starting"
                        If OnProgress IsNot Nothing Then OnProgress("正在连接房主……")
                    Case "guest-ok"
                        Logger.Info("已加入 Terracotta 房间")
                        Return
                    Case "exception", "fatal"
                        Throw New Exception("加入房间失败，请确认房间码是否正确。" & vbCrLf & TcFetchLog())
                End Select
            End If
            If Watch.ElapsedMilliseconds > TcMaxWait Then Throw New Exception("加入房间超时，请检查网络后重试。")
        Loop
    End Sub

    ''' <summary>停止 Terracotta 并清理。即使失败也不会抛出异常。</summary>
    Public Sub TcStop()
        TcRoomCode = Nothing
        If TcHttpPort > 0 AndAlso TcRunning Then
            Try : TcRequest("/panic?peaceful=true") : Catch : End Try
            Thread.Sleep(600)
        End If
        If TcProcess IsNot Nothing Then
            Try
                If Not TcProcess.HasExited Then
                    TcProcess.Kill()
                    TcProcess.WaitForExit(3000)
                End If
            Catch ex As Exception
                Logger.Warn(ex, "结束 Terracotta 客户端失败")
            Finally
                Try : TcProcess.Dispose() : Catch : End Try
                TcProcess = Nothing
            End Try
        End If
        TcHttpPort = 0
    End Sub

#End Region

End Module