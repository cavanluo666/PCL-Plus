Imports System.Reflection
Imports System.Text.RegularExpressions
Imports System.IO

''' <summary>
''' 本地翻译引擎：自动下载 llama.cpp 运行时 + Haidass 中英翻译模型（143M，Q8 量化），
''' 并在本机启动 OpenAI 兼容服务，实现完全离线的 Mod 介绍翻译。
''' </summary>
Public Module ModLocalTranslate

#Region "路径与下载源"

    ''' <summary>本地引擎数据目录。</summary>
    Public ReadOnly Property LocalAiFolder As String
        Get
            Return Paths.Base & "PCL\LocalAi\"
        End Get
    End Property

    ''' <summary>llama.cpp 运行时版本。</summary>
    Public Const EngineBuild As String = "b12007"
    ''' <summary>运行时压缩包文件名。</summary>
    Public ReadOnly Property EngineArchive As String
        Get
            Return $"llama-{EngineBuild}-bin-win-cpu-x64.zip"
        End Get
    End Property

    ''' <summary>
    ''' 运行时下载镜像。GitHub 直链在国内不通，这里按实测速度排序
    ''' （来源：EasyTier 官网提供的 GitHub 加速列表）。
    ''' </summary>
    Public ReadOnly Property EngineMirrors As String() = {
        $"https://v6.gh-proxy.org/https://github.com/ggml-org/llama.cpp/releases/download/{EngineBuild}/{EngineArchive}",
        $"https://hk.gh-proxy.org/https://github.com/ggml-org/llama.cpp/releases/download/{EngineBuild}/{EngineArchive}",
        $"https://gh-proxy.com/https://github.com/ggml-org/llama.cpp/releases/download/{EngineBuild}/{EngineArchive}",
        $"https://cdn.gh-proxy.org/https://github.com/ggml-org/llama.cpp/releases/download/{EngineBuild}/{EngineArchive}",
        $"https://edgeone.gh-proxy.org/https://github.com/ggml-org/llama.cpp/releases/download/{EngineBuild}/{EngineArchive}",
        $"https://ghproxy.net/https://github.com/ggml-org/llama.cpp/releases/download/{EngineBuild}/{EngineArchive}"
    }

    ''' <summary>运行环境压缩包的已知大小（用于显示进度）。</summary>
    Public Const EngineSizeBytes As Long = 24559587

    ''' <summary>当前选中的翻译模型条目（仓库|文件名|显示名|大小MB）。</summary>
    Public Function SelectedModelEntry() As String()
        Dim Index As Integer = Settings.Get(Of Integer)("AiTranslateModel")
        If Index < 0 OrElse Index >= AiTranslateModels.Length Then Index = 0
        Return AiTranslateModels(Index).Split("|"c)
    End Function

    ''' <summary>翻译模型所在的 HuggingFace 仓库。</summary>
    Public ReadOnly Property ModelRepo As String
        Get
            Return SelectedModelEntry()(0)
        End Get
    End Property

    ''' <summary>翻译模型文件名。</summary>
    Public ReadOnly Property ModelFile As String
        Get
            Return SelectedModelEntry()(1)
        End Get
    End Property

    ''' <summary>当前模型的大致大小（字节），用于显示进度。</summary>
    Public ReadOnly Property ModelSizeBytes As Long
        Get
            Dim Parts As String() = SelectedModelEntry()
            If Parts.Length >= 4 Then Return CLng(Parts(3)) * 1024L * 1024
            Return 0
        End Get
    End Property

    ''' <summary>模型下载镜像。</summary>
    Public ReadOnly Property ModelMirrors As String()
        Get
            Return {
                $"https://hf-mirror.com/{ModelRepo}/resolve/main/{ModelFile}",
                $"https://huggingface.co/{ModelRepo}/resolve/main/{ModelFile}"
            }
        End Get
    End Property

    ''' <summary>解压后的 llama-server 可执行文件路径。</summary>
    Public ReadOnly Property ServerPath As String
        Get
            Return LocalAiFolder & "llama-server.exe"
        End Get
    End Property

    ''' <summary>翻译模型路径。</summary>
    Public ReadOnly Property ModelPath As String
        Get
            Return LocalAiFolder & ModelFile
        End Get
    End Property

    ''' <summary>运行时压缩包缓存路径。</summary>
    Public ReadOnly Property EngineArchivePath As String
        Get
            Return LocalAiFolder & EngineArchive
        End Get
    End Property

#End Region

#Region "状态"

    ''' <summary>运行时是否已就绪。</summary>
    Public ReadOnly Property EngineReady As Boolean
        Get
            Return FileUtils.Exists(ServerPath)
        End Get
    End Property

    ''' <summary>翻译模型是否已就绪。</summary>
    Public ReadOnly Property ModelReady As Boolean
        Get
            Return FileUtils.Exists(ModelPath)
        End Get
    End Property

    ''' <summary>服务是否正在运行。</summary>
    Public ReadOnly Property ServerRunning As Boolean
        Get
            Return LocalServer IsNot Nothing AndAlso Not LocalServer.HasExited
        End Get
    End Property

    Private LocalServer As Process
    Private LocalPort As Integer = 0
    ''' <summary>是否有安装任务正在执行（防止重复点击）。</summary>
    Private Installing As Boolean = False
    Private ReadOnly InstallLock As New Object

    ''' <summary>服务端口；未运行时返回 0。</summary>
    Public ReadOnly Property ServerPort As Integer
        Get
            Return If(ServerRunning, LocalPort, 0)
        End Get
    End Property

    ''' <summary>一句话描述当前状态。</summary>
    Public Function StatusText() As String
        If Not EngineReady Then Return "尚未下载运行环境"
        If Not ModelReady Then Return "运行环境已就绪，尚未下载翻译模型"
        If ServerRunning Then Return $"已就绪并正在运行（端口 {LocalPort}）"
        Return "已就绪，尚未启动"
    End Function

#End Region

#Region "下载"

    ''' <summary>按顺序尝试多个镜像下载，返回实际使用的地址。OnProgress 回调进度（0~1）。</summary>
    Private Function DownloadWithMirrors(Mirrors As String(), Target As String, Name As String,
                                        Optional OnProgress As Action(Of Double) = Nothing) As String
        DirectoryUtils.Create(IO.Path.GetDirectoryName(Target))
        Dim LastError As Exception = Nothing
        For Each Url As String In Mirrors
            Try
                Logger.Info($"开始下载{Name}：{Url}")
                DownloadFast(Url, Target, Name, OnProgress)
                Logger.Info($"{Name}下载完成：{Target}")
                Return Url
            Catch ex As Exception
                LastError = ex
                Logger.Warn(ex, $"{Name}从该镜像下载失败，尝试下一个")
                Try
                    If FileUtils.Exists(Target) Then FileUtils.Delete(Target, False)
                Catch
                End Try
            End Try
        Next
        Throw New Exception($"{Name}的所有下载源都失败了。" & vbCrLf & If(LastError IsNot Nothing, LastError.Message, ""))
    End Function

    ''' <summary>
    ''' 下载单个文件：内置 aria2 优先（多线程，极快），其次自实现流式下载，最后启动器下载引擎。
    ''' </summary>
    Private Sub DownloadFast(Url As String, Target As String, Name As String, OnProgress As Action(Of Double))
        Dim Expected As Long = If(Name = "运行环境", EngineSizeBytes, ModelSizeBytes)
        Try
            If DownloadByAria(Url, Target, Expected, OnProgress) Then Return
        Catch ex As Exception
            Logger.Warn(ex, $"{Name}使用内置 aria2 下载失败")
        End Try
        Try
            DownloadStream(Url, Target, OnProgress)
            Return
        Catch ex As Exception
            Logger.Warn(ex, $"{Name}流式下载失败，改用启动器下载引擎重试")
            Try
                If FileUtils.Exists(Target) Then FileUtils.Delete(Target, False)
            Catch
            End Try
        End Try
        DownloadByLoader(Url, Target, Name, OnProgress)
    End Sub

    ''' <summary>自实现的流式下载：单线程、逐块写盘、进度实时回调。</summary>
    Private Sub DownloadStream(Url As String, Target As String, OnProgress As Action(Of Double))
        Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
        Req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
        Req.Timeout = 30000
        Req.ReadWriteTimeout = 60000
        Req.AllowAutoRedirect = True
        Dim Temp As String = Target & ".part"
        Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
            Dim Total As Long = Resp.ContentLength
            If Total <= 0 Then Throw New Exception("服务器未返回文件大小")
            Using Fs As New IO.FileStream(Temp, IO.FileMode.Create, IO.FileAccess.Write)
                Using Src As IO.Stream = Resp.GetResponseStream()
                    Dim Buf(131071) As Byte
                    Dim Got As Long = 0
                    Dim Watch As Stopwatch = Stopwatch.StartNew()
                    Dim NextReport As Long = 0
                    Do
                        Dim N As Integer = Src.Read(Buf, 0, Buf.Length)
                        If N <= 0 Then Exit Do
                        Fs.Write(Buf, 0, N)
                        Got += N
                        If Watch.ElapsedMilliseconds >= NextReport Then
                            NextReport = Watch.ElapsedMilliseconds + 200
                            If OnProgress IsNot Nothing Then OnProgress(Got / CDbl(Total))
                        End If
                    Loop
                    If OnProgress IsNot Nothing Then OnProgress(1)
                    If Got <> Total Then Throw New Exception($"下载不完整：{Got} / {Total} 字节")
                End Using
            End Using
        End Using
        If FileUtils.Exists(Target) Then FileUtils.Delete(Target, False)
        IO.File.Move(Temp, Target)
    End Sub

    ''' <summary>回退方案：启动器自带的多线程下载引擎（进度经 LoaderToSyncProgress 承接）。</summary>
    Private Sub DownloadByLoader(Url As String, Target As String, Name As String, OnProgress As Action(Of Double))
        Dim Container As New LoaderTask(Of Integer, Integer)($"下载{Name}",
                                                             Sub(Task As LoaderTask(Of Integer, Integer)) Task.Progress = 1)
        Dim Running As Boolean = True
        Dim Reporter As Threading.Thread = Nothing
        If OnProgress IsNot Nothing Then
            Reporter = New Threading.Thread(
                Sub()
                    Dim Last As Double = -999
                    Do While Running
                        Try
                            Dim Current As Double = Container.Progress
                            If Math.Abs(Current - Last) > 0.0005 Then
                                Last = Current
                                OnProgress(Current)
                            End If
                        Catch
                        End Try
                        Thread.Sleep(200)
                    Loop
                End Sub)
            Reporter.IsBackground = True
            Reporter.Start()
        End If
        Try
            NetDownloadByLoader({Url}, Target, Container, New FileChecker With {.MinSize = 1024 * 1024})
        Finally
            Running = False
        End Try
        If Not FileUtils.Exists(Target) Then Throw New Exception($"{Name}下载完成后文件不存在")
    End Sub

    ''' <summary>内置 aria2c 解压出来的路径。</summary>
    Public ReadOnly Property AriaPath As String
        Get
            Return LocalAiFolder & "aria2c.exe"
        End Get
    End Property

    ''' <summary>把内置的 aria2c 释放到本地目录；没有内置资源时返回 False。</summary>
    Private Function ExtractBuiltinAria() As Boolean
        Try
            If FileUtils.Exists(AriaPath) Then Return True
            Using Stream As IO.Stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("aria2c.exe")
                If Stream Is Nothing Then Return False
                DirectoryUtils.Create(LocalAiFolder)
                Using Fs As New IO.FileStream(AriaPath, IO.FileMode.Create, IO.FileAccess.Write)
                    Stream.CopyTo(Fs)
                End Using
            End Using
            Logger.Info($"已释放内置 aria2c（{New IO.FileInfo(AriaPath).Length} 字节）")
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "释放内置 aria2c 失败")
            Return False
        End Try
    End Function

    ''' <summary>用内置 aria2 多线程下载，进度靠轮询文件大小。</summary>
    Private Function DownloadByAria(Url As String, Target As String, ExpectedSize As Long,
                                    OnProgress As Action(Of Double)) As Boolean
        If Not ExtractBuiltinAria() Then Return False
        Dim Folder As String = IO.Path.GetDirectoryName(Target)
        DirectoryUtils.Create(Folder)
        Try
            If FileUtils.Exists(Target) Then FileUtils.Delete(Target, False)
        Catch
        End Try
        Dim Args As String =
            $"-x 16 -s 16 -k 1M -d ""{Folder}"" -o ""{IO.Path.GetFileName(Target)}"" " &
            "--allow-overwrite=true --auto-file-renaming=false --file-allocation=none " &
            $"--console-log-level=error --summary-interval=0 --check-certificate=false --retry-wait=2 --max-tries=5 ""{Url}"""
        Dim Info As New ProcessStartInfo(AriaPath) With {
            .Arguments = Args,
            .WorkingDirectory = Folder,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True}
        Dim Proc As Process = Process.Start(Info)
        If Proc Is Nothing Then Throw New Exception("无法启动 aria2c")
        '后台读取输出，避免管道写满导致 aria2 阻塞
        Dim Dummy As New Threading.Thread(
            Sub()
                Try : Proc.StandardOutput.ReadToEnd() : Catch : End Try
            End Sub)
        Dummy.IsBackground = True
        Dummy.Start()
        Do While Not Proc.HasExited
            Thread.Sleep(200)
            Try
                If IO.File.Exists(Target) AndAlso ExpectedSize > 0 AndAlso OnProgress IsNot Nothing Then
                    Dim Got As Long = New IO.FileInfo(Target).Length
                    OnProgress(Math.Min(1, Got / CDbl(ExpectedSize)))
                End If
            Catch
            End Try
        Loop
        Dim Code As Integer = Proc.ExitCode
        Proc.Dispose()
        If Code <> 0 Then Throw New Exception($"aria2c 退出码 {Code}")
        If Not FileUtils.Exists(Target) Then Throw New Exception("aria2c 下载后文件不存在")
        If OnProgress IsNot Nothing Then OnProgress(1)
        Return True
    End Function

    ''' <summary>下载并解压 llama.cpp 运行时。</summary>
    Public Sub InstallEngine(Optional OnProgress As Action(Of Double) = Nothing)
        SyncLock InstallLock
            If Installing Then Throw New Exception("已有安装任务正在进行中，请等待它结束。")
            Installing = True
        End SyncLock
        Try
            InstallEngineInternal(OnProgress)
        Finally
            Installing = False
        End Try
    End Sub

    Private Sub InstallEngineInternal(OnProgress As Action(Of Double))
        DirectoryUtils.Create(LocalAiFolder)
        DownloadWithMirrors(EngineMirrors, EngineArchivePath, "运行环境", OnProgress)
        Logger.Info("正在解压运行环境……")
        FileUtils.ExtractToDirectory(EngineArchivePath, LocalAiFolder)
        If Not FileUtils.Exists(ServerPath) Then
            Dim Found As String = DirectoryUtils.EnumerateFiles(LocalAiFolder, True, "llama-server.exe").FirstOrDefault()
            If Found IsNot Nothing Then
                For Each Item As String In DirectoryUtils.EnumerateFiles(IO.Path.GetDirectoryName(Found), False)
                    FileUtils.Copy(Item, LocalAiFolder & IO.Path.GetFileName(Item))
                Next
            End If
        End If
        FileUtils.Delete(EngineArchivePath, False)
        If Not EngineReady Then Throw New Exception("解压后没有找到 llama-server.exe，运行环境可能不完整。")
        Logger.Info("运行环境安装完成")
    End Sub

    ''' <summary>下载翻译模型。</summary>
    Public Sub InstallModel(Optional OnProgress As Action(Of Double) = Nothing)
        SyncLock InstallLock
            If Installing Then Throw New Exception("已有安装任务正在进行中，请等待它结束。")
            Installing = True
        End SyncLock
        Try
            DirectoryUtils.Create(LocalAiFolder)
            DownloadWithMirrors(ModelMirrors, ModelPath, "翻译模型", OnProgress)
        Finally
            Installing = False
        End Try
    End Sub

    ''' <summary>是否正在后台准备。</summary>
    Private Preparing As Boolean = False

    ''' <summary>
    ''' 启动时后台静默准备本地翻译：缺运行环境/模型就下载，就绪后自动启动服务。
    ''' 由设置 LocalAiAutoPrepare 控制，全过程中不做任何弹窗打扰。
    ''' </summary>
    Public Sub PrepareInBackground()
        If Not Settings.Get(Of Boolean)("LocalAiAutoPrepare") Then Return
        If Preparing Then Return
        If EngineReady AndAlso ModelReady AndAlso ServerRunning Then Return
        Preparing = True
        RunInNewThread(
        Sub()
            Try
                If Not EngineReady Then
                    Logger.Info("[本地翻译] 运行环境缺失，开始后台静默下载")
                    InstallEngine()
                    Logger.Info("[本地翻译] 运行环境准备完成")
                End If
                If Not ModelReady Then
                    Logger.Info($"[本地翻译] 翻译模型缺失（{ModelFile}），开始后台静默下载")
                    InstallModel()
                    Logger.Info("[本地翻译] 翻译模型准备完成")
                End If
                If EngineReady AndAlso ModelReady AndAlso Not ServerRunning Then
                    Logger.Info("[本地翻译] 自动启动本地服务")
                    StartServer()
                    If WaitServerReady(60000) Then
                        Logger.Info("[本地翻译] 服务已就绪")
                    Else
                        Logger.Warn("[本地翻译] 服务启动超时")
                    End If
                End If
            Catch ex As Exception
                Logger.Warn(ex, "[本地翻译] 后台准备失败")
            Finally
                Preparing = False
            End Try
            '顺便预探测 Google 翻译可用地址（后台跑，首次用 Google 翻译时就不用等）
            Try
                ModGoogleTranslate.EnsureIps()
            Catch ex As Exception
                Logger.Warn(ex, "预探测 Google 翻译地址失败")
            End Try
        End Sub, "本地翻译后台准备")
    End Sub

    ''' <summary>删除运行环境与模型。</summary>
    Public Sub UninstallAll()
        StopServer()
        Try
            If DirectoryUtils.Exists(LocalAiFolder) Then DirectoryUtils.Delete(LocalAiFolder)
        Catch ex As Exception
            Logger.Warn(ex, "删除本地引擎目录失败")
        End Try
    End Sub

#End Region

#Region "服务控制"

    ''' <summary>启动本地翻译服务（会自动选一个空闲端口）。</summary>
    Public Sub StartServer()
        SyncLock InstallLock
            If ServerRunning Then Return
        If Not EngineReady Then Throw New Exception("还没有下载运行环境。")
        If Not ModelReady Then Throw New Exception("还没有下载翻译模型。")
        Dim Ports As List(Of Integer) = FindFreePorts(1)
        LocalPort = Ports(0)
        Dim Info As New ProcessStartInfo(ServerPath) With {
            .Arguments = $"-m ""{ModelPath}"" --host 127.0.0.1 --port {LocalPort} -c 2048 --no-webui",
            .WorkingDirectory = LocalAiFolder,
            .UseShellExecute = False,
            .CreateNoWindow = True}
        LocalServer = Process.Start(Info)
        If LocalServer Is Nothing Then Throw New Exception("无法启动本地翻译服务。")
            Logger.Info($"本地翻译服务已启动，端口 {LocalPort}")
        End SyncLock
    End Sub

    ''' <summary>等待服务可以响应请求。</summary>
    Public Function WaitServerReady(TimeoutMs As Integer) As Boolean
        If Not ServerRunning Then Return False
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Do While Watch.ElapsedMilliseconds < TimeoutMs
            Thread.Sleep(300)
            If Not ServerRunning Then Return False
            Try
                NetRequestByClient($"http://127.0.0.1:{LocalPort}/health", Timeout:=3000, MakeLog:=False)
                Return True
            Catch
            End Try
        Loop
        Return False
    End Function

    ''' <summary>停止本地翻译服务。</summary>
    Public Sub StopServer()
        If LocalServer Is Nothing Then Return
        Try
            If Not LocalServer.HasExited Then
                LocalServer.Kill()
                LocalServer.WaitForExit(3000)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "停止本地翻译服务失败")
        Finally
            Try : LocalServer.Dispose() : Catch : End Try
            LocalServer = Nothing
            LocalPort = 0
        End Try
    End Sub

#End Region

#Region "翻译"

    ''' <summary>确保服务可用（必要时自动启动并等待就绪）。</summary>
    Public Sub EnsureServer()
        If Not ServerRunning Then
            StartServer()
            If Not WaitServerReady(60000) Then Throw New Exception("本地翻译服务启动超时。")
        End If
    End Sub

    ''' <summary>
    ''' 把一个英文文本翻译成中文（离线）。长文本会自动分段，逐段翻译后拼接。
    ''' OnProgress 回调 (已完成段数, 总段数)。
    ''' </summary>
    Public Function TranslateToChinese(Text As String, Optional OnProgress As Action(Of Integer, Integer) = Nothing) As String
        If String.IsNullOrWhiteSpace(Text) Then Return ""
        EnsureServer()
        Dim Chunks As List(Of String) = SplitForTranslate(Text)
        If Chunks.Count = 0 Then Return ""
        Dim Result As New Text.StringBuilder
        For i = 0 To Chunks.Count - 1
            Try
                Dim Piece As String = TranslateChunk(Chunks(i))
                If Piece <> "" Then
                    If Result.Length > 0 Then Result.AppendLine().AppendLine()
                    Result.Append(Piece)
                End If
            Catch ex As Exception
                Logger.Warn(ex, $"第 {i + 1} 段翻译失败，保留原文")
                If Result.Length > 0 Then Result.AppendLine().AppendLine()
                Result.Append(Chunks(i))
            End Try
            If OnProgress IsNot Nothing Then OnProgress(i + 1, Chunks.Count)
        Next
        Return Result.ToString()
    End Function

    ''' <summary>
    ''' 把长文本拆成适合小模型处理的段落。
    ''' 实测这类小模型对过短的输入会胡言乱语（甚至吐出 "1. 2. 3." 这种重复），
    ''' 所以这里把多行合并到 TargetLength 以上再送进去，同时不超过 MaxLength。
    ''' </summary>
    Private Function SplitForTranslate(Text As String) As List(Of String)
        Const MaxLength As Integer = 400
        Const TargetLength As Integer = 220
        Dim Result As New List(Of String)
        Dim Buf As New Text.StringBuilder
        For Each Line As String In Text.Replace(vbCrLf, vbLf).Replace(ChrW(13), vbLf).Split(ChrW(10))
            Dim Trimmed As String = Line.Trim
            If Trimmed = "" Then Continue For
            'Markdown 结构行不翻译
            If Trimmed.StartsWith("#") OrElse Trimmed.StartsWith("![") OrElse Trimmed.StartsWith("```") Then Continue For
            For Each Piece As String In SplitLongLine(Trimmed, MaxLength)
                If Buf.Length > 0 AndAlso Buf.Length + Piece.Length + 1 > MaxLength Then
                    Result.Add(Buf.ToString())
                    Buf.Clear()
                End If
                If Buf.Length > 0 Then Buf.Append(vbLf)
                Buf.Append(Piece)
                If Buf.Length >= TargetLength Then
                    Result.Add(Buf.ToString())
                    Buf.Clear()
                End If
            Next
        Next
        If Buf.Length > 0 Then Result.Add(Buf.ToString())
        Return Result
    End Function

    ''' <summary>把过长的单行按句子切成不超过 MaxLength 的片段。</summary>
    Private Function SplitLongLine(Line As String, MaxLength As Integer) As List(Of String)
        Dim Result As New List(Of String)
        If Line.Length <= MaxLength Then
            Result.Add(Line)
            Return Result
        End If
        Dim Buf As New Text.StringBuilder
        For Each Piece As String In Regex.Split(Line, "(?<=[.!?;:])\s+")
            If Buf.Length > 0 AndAlso Buf.Length + Piece.Length > MaxLength Then
                Result.Add(Buf.ToString())
                Buf.Clear()
            End If
            If Buf.Length > 0 Then Buf.Append(" "c)
            Buf.Append(Piece)
        Next
        If Buf.Length > 0 Then Result.Add(Buf.ToString())
        Return Result
    End Function

    Private Function TranslateChunk(Text As String) As String
        Dim Result As String = RequestTranslation(Text)
        '143M 的小模型偶尔会陷入重复输出（比如一直吐 "1. 2. 3."），检测到就重试一次
        If LooksRepetitive(Result) Then
            Logger.Warn("翻译输出疑似重复，重试一次")
            Dim Retry As String = RequestTranslation(Text)
            If Not LooksRepetitive(Retry) Then Result = Retry
        End If
        Return Result
    End Function

    Private Function RequestTranslation(Text As String) As String
        Dim Messages As New JArray
        Messages.Add(New JObject From {
            {"role", "user"},
            {"content", "Translate the following text from English to Simplified Chinese." & vbLf & Text}})
        Dim Body As New JObject From {
            {"model", "local"},
            {"messages", Messages},
            {"temperature", 0.0},
            {"max_tokens", Math.Max(96, Math.Min(384, Text.Length * 2 + 48))},
            {"stream", False}}
        Dim Response As String = NetRequestByClient($"http://127.0.0.1:{LocalPort}/v1/chat/completions",
                                                   HttpMethod.Post, Body.ToString(), "application/json",
                                                   Timeout:=120000, RequireJson:=True, MakeLog:=False)
        Dim Json As JObject = Response.DeserializeJson()
        Dim Choices = Json("choices")
        If Choices Is Nothing OrElse Choices.Count = 0 Then Return ""
        Return StripThink(Choices(0)("message")("content").ToString())
    End Function

    ''' <summary>去掉模型输出里的思考段。</summary>
    Private Function StripThink(Text As String) As String
        If String.IsNullOrEmpty(Text) Then Return ""
        If Text.Contains("<think") Then
            Text = Regex.Replace(Text, "<think[^>]*>.*?</think\s*>", "", RegexOptions.Singleline Or RegexOptions.IgnoreCase)
            Dim LastEnd As Integer = Text.LastIndexOf("</think", StringComparison.OrdinalIgnoreCase)
            If LastEnd >= 0 Then Text = Text.Substring(LastEnd + 8)
            Text = Text.Replace("<think>", "").Replace("</think>", "")
        End If
        Return Text.Trim()
    End Function

    ''' <summary>粗略判断输出是否陷入重复。</summary>
    Private Function LooksRepetitive(Text As String) As Boolean
        If String.IsNullOrEmpty(Text) OrElse Text.Length < 60 Then Return False
        Dim Start As Integer = Text.Length \ 2
        Dim Probe As String = Text.Substring(Start, Math.Min(20, Text.Length - Start))
        If Probe.Trim = "" Then Return False
        Dim Count As Integer = 0
        Dim Index As Integer = 0
        Do
            Index = Text.IndexOf(Probe, Index, StringComparison.Ordinal)
            If Index < 0 Then Exit Do
            Count += 1
            Index += Probe.Length
        Loop
        Return Count >= 4
    End Function

#End Region

End Module
