Imports System.Net
Imports System.Net.Sockets
Imports System.Threading.Tasks

''' <summary>
''' 使用 Google 翻译接口（translate-pa.googleapis.com）做英译中。
''' 该域名在国内受 DNS 污染影响，这里改为「IP 直连 + 保持 SNI/Host 不变」，
''' 因此需要关闭证书吊销检查（IP 直连时访问不到 CRL/OCSP 服务器）。
''' IP 列表来自公开的 Ponderfly/GoogleTranslateIpCheck 项目（MIT）。
''' </summary>
Public Module ModGoogleTranslate

    Private Const TranslateUrl As String = "https://translate-pa.googleapis.com/v1/translateHtml"
    Private Const HostName As String = "translate-pa.googleapis.com"
    ''' <summary>Google 网页版翻译自带的公开 API Key。</summary>
    Private Const PublicApiKey As String = "AIzaSyATBXajvzQLTDHEQbcpq0Ihe0vWDHmO520"
    ''' <summary>公开的 Google 翻译可用 IP 列表（走 GitHub 代理）。</summary>
    Private Const IpListUrl As String = "https://gh-proxy.com/https://raw.githubusercontent.com/Ponderfly/GoogleTranslateIpCheck/refs/heads/master/src/GoogleTranslateIpCheck/GoogleTranslateIpCheck/ip.txt"
    ''' <summary>备用 IP：列表下载失败时使用。</summary>
    Private ReadOnly FallbackIps As String() = {
        "8.137.83.3", "8.137.102.117", "8.138.131.24", "8.138.21.175",
        "8.134.173.202", "47.104.71.109", "47.98.142.11", "101.200.234.11"
    }
    Private Const IpCacheFile As String = "google_ips.txt"
    Private Const ProbeCount As Integer = 120
    Private Const ProbeConcurrency As Integer = 32

    Private ReadOnly GoodIps As New List(Of String)
    Private ReadOnly IpLock As New Object
    Private LastProbe As Date = Date.MinValue
    Private IpInitDone As Boolean = False

    ''' <summary>当前是否有可用的 IP。</summary>
    Public ReadOnly Property HasIp As Boolean
        Get
            SyncLock IpLock
                Return GoodIps.Count > 0
            End SyncLock
        End Get
    End Property

    ''' <summary>确保已经探测出可用 IP（首次调用会下载列表并测速，之后按 30 分钟缓存）。</summary>
    Public Sub EnsureIps(Optional Force As Boolean = False)
        SyncLock IpLock
            If Not Force AndAlso IpInitDone AndAlso (Date.Now - LastProbe).TotalMinutes < 30 Then Return
        End SyncLock

        Dim Candidates As New List(Of String)
        '先读本地缓存
        Try
            Dim CachePath As String = ModLocalTranslate.LocalAiFolder & IpCacheFile
            If FileUtils.Exists(CachePath) Then
                For Each Line As String In FileUtils.ReadAsString(CachePath).Split(ChrW(10))
                    Dim Trimmed As String = Line.Trim
                    If Trimmed <> "" Then Candidates.Add(Trimmed)
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取 Google IP 缓存失败")
        End Try

        '缓存太少就重新下载列表
        If Candidates.Count < 8 Then
            Try
                Logger.Info("正在下载 Google 翻译可用 IP 列表……")
                Dim ListText As String = NetRequestByClient(IpListUrl, Timeout:=25000, MakeLog:=False)
                Candidates.Clear()
                For Each Line As String In ListText.Replace(vbCrLf, vbLf).Split(ChrW(10))
                    Dim Trimmed As String = Line.Trim
                    If Trimmed <> "" AndAlso Trimmed.Contains(".") Then Candidates.Add(Trimmed)
                Next
                Logger.Info($"IP 列表已获取，共 {Candidates.Count} 个候选")
            Catch ex As Exception
                Logger.Warn(ex, "下载 Google IP 列表失败，改用内置备用地址")
            End Try
        End If

        If Candidates.Count = 0 Then Candidates.AddRange(FallbackIps)

        '并发测速，取前 ProbeCount 个候选
        Dim Pool As List(Of String) = Candidates
        If Pool.Count > ProbeCount Then Pool = Pool.GetRange(0, ProbeCount)
        Dim Reachable As New List(Of String)
        Dim ReachLock As New Object
        Dim Index As Integer = -1
        Dim Workers As New List(Of Task)
        For W As Integer = 1 To ProbeConcurrency
            Workers.Add(Task.Run(
                Sub()
                    Do
                        Dim Current As Integer = Threading.Interlocked.Increment(Index)
                        If Current >= Pool.Count Then Exit Do
                        If ProbeIp(Pool(Current)) Then
                            SyncLock ReachLock
                                Reachable.Add(Pool(Current))
                            End SyncLock
                        End If
                    Loop
                End Sub))
        Next
        Try
            Task.WaitAll(Workers.ToArray(), 60000)
        Catch ex As Exception
            Logger.Warn(ex, "探测 Google IP 超时")
        End Try

        'TCP 通不代表证书正确（列表里混有证书不受信任的地址），
        '所以再对候选做一次真实的翻译请求，只留下真正可用的
        Dim Verified As New List(Of String)
        Dim VerifyLock As New Object
        Dim VerifyList As List(Of String) = Reachable
        If VerifyList.Count > 40 Then VerifyList = VerifyList.GetRange(0, 40)
        Dim VerifyIndex As Integer = -1
        Dim VerifyWorkers As New List(Of Task)
        For W As Integer = 1 To 8
            VerifyWorkers.Add(Task.Run(
                Sub()
                    Do
                        Dim Current As Integer = Threading.Interlocked.Increment(VerifyIndex)
                        If Current >= VerifyList.Count Then Exit Do
                        If VerifyIp(VerifyList(Current)) Then
                            SyncLock VerifyLock
                                Verified.Add(VerifyList(Current))
                            End SyncLock
                        End If
                    Loop
                End Sub))
        Next
        Try
            Task.WaitAll(VerifyWorkers.ToArray(), 40000)
        Catch ex As Exception
            Logger.Warn(ex, "验证 Google IP 超时")
        End Try
        If Verified.Count > 0 Then Reachable = Verified

        SyncLock IpLock
            GoodIps.Clear()
            If Reachable.Count > 0 Then
                '把能连通的排前面
                For Each Ip As String In Reachable
                    GoodIps.Add(Ip)
                Next
            Else
                Logger.Warn("没有探测到可用的 Google IP，使用内置备用地址")
                GoodIps.AddRange(FallbackIps)
            End If
            LastProbe = Date.Now
            IpInitDone = True
        End SyncLock
        Logger.Info($"Google 翻译可用 IP：{Reachable.Count} 个（候选 {Pool.Count} 个）")
        SaveIpCache()
    End Sub

    ''' <summary>验证一个 IP 能否真正完成翻译（TCP 通不代表证书正确）。</summary>
    Private Function VerifyIp(Ip As String) As Boolean
        Try
            Dim Inner As New JArray
            Inner.Add("ok")
            Dim Meta As New JArray
            Meta.Add(Inner)
            Meta.Add("en")
            Meta.Add("zh-CN")
            Dim Payload As New JArray
            Payload.Add(Meta)
            Payload.Add("wt_lib")
            Dim Result As String = SendToIp(Payload.ToString(Newtonsoft.Json.Formatting.None), Ip)
            Return Not String.IsNullOrWhiteSpace(Result)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>测一个 IP 的 443 端口是否可连。</summary>
    Private Function ProbeIp(Ip As String) As Boolean
        Dim Client As TcpClient = Nothing
        Try
            Client = New TcpClient
            Dim Task As Task = Client.ConnectAsync(Ip, 443)
            If Task.Wait(2500) AndAlso Client.Connected Then Return True
        Catch
        Finally
            If Client IsNot Nothing Then
                Try : Client.Close() : Catch : End Try
            End If
        End Try
        Return False
    End Function

    Private Sub SaveIpCache()
        Try
            DirectoryUtils.Create(ModLocalTranslate.LocalAiFolder)
            SyncLock IpLock
                FileUtils.Write(ModLocalTranslate.LocalAiFolder & IpCacheFile, String.Join(vbCrLf, GoodIps))
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, "写入 Google IP 缓存失败")
        End Try
    End Sub

    ''' <summary>标记某个 IP 不可用（下次优先换别的）。</summary>
    Private Sub MarkBad(Ip As String)
        SyncLock IpLock
            GoodIps.Remove(Ip)
            If GoodIps.Count = 0 Then GoodIps.AddRange(FallbackIps)
        End SyncLock
    End Sub

    ''' <summary>
    ''' 把 Markdown 文本逐行翻译成中文。
    ''' 接口支持一次提交多段并逐段返回，所以这里按行分批（每批不超过 4000 字符），
    ''' 这样换行和列表结构都能保留下来。失败会抛异常，由上层决定回退。
    ''' </summary>
    Public Function Translate(Text As String) As String
        EnsureIps()
        Dim Lines As String() = Text.Replace(vbCrLf, vbLf).Replace(ChrW(13), vbLf).Split(ChrW(10))
        Dim Result As New List(Of String)
        Dim BatchTexts As New List(Of String)
        Dim BatchIndex As New List(Of Integer)
        For i = 0 To Lines.Length - 1
            Dim Line As String = Lines(i)
            Result.Add(Line) '先放原文，翻译成功后替换
            Dim Trimmed As String = Line.Trim
            '空行与纯结构行（图片、代码围栏）不翻译
            If Trimmed = "" OrElse Trimmed.StartsWith("![") OrElse Trimmed.StartsWith("```") Then Continue For
            If BatchTexts.Count >= 80 OrElse (BatchTexts.Count > 0 AndAlso LengthOf(BatchTexts) + Line.Length > 4000) Then
                ApplyBatch(BatchTexts, BatchIndex, Result)
            End If
            BatchTexts.Add(Line)
            BatchIndex.Add(i)
        Next
        If BatchTexts.Count > 0 Then ApplyBatch(BatchTexts, BatchIndex, Result)
        Return String.Join(vbLf, Result)
    End Function

    Private Function LengthOf(Items As List(Of String)) As Integer
        Dim Total As Integer = 0
        For Each Item As String In Items
            Total += Item.Length
        Next
        Return Total
    End Function

    ''' <summary>翻译一批行并写回结果列表。</summary>
    Private Sub ApplyBatch(Texts As List(Of String), Indexes As List(Of Integer), Result As List(Of String))
        Try
            Dim Translated As List(Of String) = TranslateBatch(Texts)
            For k As Integer = 0 To Indexes.Count - 1
                If k < Translated.Count AndAlso Not String.IsNullOrWhiteSpace(Translated(k)) Then
                    Result(Indexes(k)) = Translated(k)
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "Google 翻译这一批失败，保留原文")
        End Try
        Texts.Clear()
        Indexes.Clear()
    End Sub

    ''' <summary>提交一批文本，返回逐段译文。</summary>
    Private Function TranslateBatch(Texts As List(Of String)) As List(Of String)
        Dim Inner As New JArray
        For Each Item As String In Texts
            Inner.Add(Item)
        Next
        Dim Meta As New JArray
        Meta.Add(Inner)
        Meta.Add("en")
        Meta.Add("zh-CN")
        Dim Payload As New JArray
        Payload.Add(Meta)
        Payload.Add("wt_lib")
        Dim Raw As String = SendRequest(Payload.ToString(Newtonsoft.Json.Formatting.None))
        Dim Json As JArray = Raw.DeserializeJson()
        Dim Output As New List(Of String)
        If Json.Count > 0 AndAlso Json(0) IsNot Nothing Then
            For Each Item As JToken In Json(0)
                If Item IsNot Nothing AndAlso Item.Count > 0 Then
                    Output.Add(Item(0).ToString())
                Else
                    Output.Add("")
                End If
            Next
        End If
        Return Output
    End Function

    ''' <summary>提交一次请求，内部会在多个可用 IP 之间轮换。</summary>
    Private Function SendRequest(Payload As String) As String
        EnsureIps()
        Dim LastError As Exception = Nothing
        For Attempt As Integer = 1 To 12
            Dim CurrentIp As String = ""
            SyncLock IpLock
                CurrentIp = If(GoodIps.Count > 0, GoodIps(0), "")
            End SyncLock
            If CurrentIp = "" Then
                EnsureIps(True)
                SyncLock IpLock
                    CurrentIp = If(GoodIps.Count > 0, GoodIps(0), "")
                End SyncLock
                If CurrentIp = "" Then Throw New Exception("没有可用的 Google 翻译地址")
            End If
            Try
                Return SendToIp(Payload, CurrentIp)
            Catch ex As Exception
                LastError = ex
                Logger.Warn(ex, $"Google 翻译经 {CurrentIp} 失败，换下一个地址")
                MarkBad(CurrentIp)
            End Try
        Next
        Throw New Exception("所有 Google 翻译地址都失败了。" & vbCrLf & If(LastError IsNot Nothing, LastError.Message, ""))
    End Function

    ''' <summary>通过指定 IP 发送一次翻译请求。</summary>
    Private Function SendToIp(Payload As String, Ip As String) As String
        'IP 直连时访问不到证书吊销服务器，需要临时关闭吊销检查；
        '用完恢复原值，避免影响启动器其它 HTTPS 请求。
        Dim OldRevoke As Boolean = Net.ServicePointManager.CheckCertificateRevocationList
        Net.ServicePointManager.CheckCertificateRevocationList = False
        Try
            Return SendToIpCore(Payload, Ip)
        Finally
            Net.ServicePointManager.CheckCertificateRevocationList = OldRevoke
        End Try
    End Function

    Private Function SendToIpCore(Payload As String, Ip As String) As String
        Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(TranslateUrl), Net.HttpWebRequest)
        Req.Method = "POST"
        Req.ContentType = "application/json+protobuf"
        Req.Headers.Add("X-Goog-API-Key", PublicApiKey)
        Req.Timeout = 20000
        Req.ReadWriteTimeout = 20000
        Req.UserAgent = "Mozilla/5.0"
        '把 TCP 连接钉到指定 IP，域名与 SNI 保持不变
        Dim Target As Net.IPAddress = Net.IPAddress.Parse(Ip)
        Req.ServicePoint.BindIPEndPointDelegate =
            Function(ServicePoint As Net.ServicePoint, RemoteEndPoint As Net.IPEndPoint, RetryCount As Integer) As Net.IPEndPoint
                Return New Net.IPEndPoint(Target, RemoteEndPoint.Port)
            End Function
        Dim Bytes As Byte() = Text.Encoding.UTF8.GetBytes(Payload)
        Req.ContentLength = Bytes.Length
        Using Output As IO.Stream = Req.GetRequestStream()
            Output.Write(Bytes, 0, Bytes.Length)
        End Using
        Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
            Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Text.Encoding.UTF8)
                Return Reader.ReadToEnd()
            End Using
        End Using
    End Function

End Module
