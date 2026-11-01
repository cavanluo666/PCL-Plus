'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "Plus"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '用于微软登录的 ClientId
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "00000000441cc96b")
    '微软登录走 MSA（login.live.com）端点，其 Scope 格式与 Azure AD 不同，不可混用
    Public Const OAuthScopeMs As String = "service%3A%3Auser.auth.xboxlive.com%3A%3AMBI_SSL"
    'CurseForge API Key
    Public CurseForgeAPIKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

Public Const RestrictMcDownload As Boolean = False
    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    ''' <summary>
    ''' 是否允许使用全部隐藏主题。
    ''' <para>False（默认）：只有官方免费主题（0、1、2、3、4、42）可用，其余主题按钮为灰色不可点击。</para>
    ''' <para>True：解锁全部隐藏主题。</para>
    ''' <para>要开启，把本常量改为 True 后重新编译 PCL 即可。</para>
    ''' </summary>
    Public Const AllowHiddenThemes As Boolean = False

    ''' <summary>
    ''' 按主题 Id 设置配色参数。
    ''' 说明：官方正式版的 ThemeRefresh 会把 NewTheme 强制置 0（IL 中可见 ldc.i4.0 / starg.s），
    ''' 导致 ThemeNow 恒为 0、第二次调用即直接返回，反编译中并不存在主题配色表。
    ''' 因此下表是依据主题名称补全的，效果不满意可直接改这里的数值。
    ''' </summary>
    Private Sub ThemeApplyColors(Id As Integer)
        Select Case Id
            Case 0 '龙猫蓝
                ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0
            Case 1 '甜柠青
                ColorHue = 172 : ColorSat = 70 : ColorLightAdjust = 5
            Case 2 '小草绿
                ColorHue = 118 : ColorSat = 65 : ColorLightAdjust = 0
            Case 3 '菠萝黄
                ColorHue = 44 : ColorSat = 88 : ColorLightAdjust = 6
            Case 4 '橡木棕
                ColorHue = 26 : ColorSat = 52 : ColorLightAdjust = -14
            Case 5 '玄素黑
                ColorHue = 210 : ColorSat = 6 : ColorLightAdjust = -40
            Case 6 '铁杆粉
                ColorHue = 336 : ColorSat = 70 : ColorLightAdjust = 6
            Case 7 '神秘紫
                ColorHue = 274 : ColorSat = 66 : ColorLightAdjust = 0
            Case 8 '秋仪金
                ColorHue = 42 : ColorSat = 60 : ColorLightAdjust = -8
            Case 9 '活跃橙
                ColorHue = 22 : ColorSat = 90 : ColorLightAdjust = 6
            Case 10 '跳票红
                ColorHue = 358 : ColorSat = 76 : ColorLightAdjust = 0
            Case 11 '极客蓝
                ColorHue = 205 : ColorSat = 92 : ColorLightAdjust = -10
            Case 12 '滑稽彩（主色沿用默认，标题栏走四段渐变）
                ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0
            Case 13 '欧皇彩
                ColorHue = 45 : ColorSat = 92 : ColorLightAdjust = 8
            Case 14 '自定义，使用设置中的 HSL
                ColorHue = Settings.Get(Of Integer)("UiLauncherHue")
                ColorSat = Settings.Get(Of Integer)("UiLauncherSat")
                ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight") - 20
            Case Else
                ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0
        End Select
    End Sub

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme
            'ThemeNow 为 -1 表示尚未指定主题（启动初期的默认配色），此时不做解锁检查
            If ThemeNow >= 0 AndAlso Not ThemeCheckOne(ThemeNow) Then Return

            ThemeApplyColors(ThemeNow)
            Select Case ThemeDontClick
                Case 1 '眼瞎白
                    ColorLightAdjust = 999
                Case 2 '真·滑稽彩
                    ColorHue = RandomInteger(0, 359)
                    ColorSat = RandomInteger(40, 70)
                    ColorLightAdjust = RandomInteger(-20, 20)
            End Select

            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
            If ThemeNow <> 12 AndAlso ThemeNow <> 14 AndAlso ThemeDontClick <> 2 Then
                Logger.Info($"刷新主题：{ThemeNow}")
            End If
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub

    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush As LinearGradientBrush
            If ThemeNow = 5 Then '玄素黑：更暗的标题栏
                Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 25)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 15)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 25)})
                FrmMain.PanTitle.Background = Brush
                FrmMain.PanTitle.Background.Freeze()
            ElseIf ThemeNow <> 12 AndAlso ThemeDontClick <> 2 Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
                Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
                FrmMain.PanTitle.Background = Brush
                FrmMain.PanTitle.Background.Freeze()
            Else '滑稽彩
                Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue - 21, ColorSat, 53 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.33, .Color = New MyColor().FromHSL2(ColorHue - 7, ColorSat, 47 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.67, .Color = New MyColor().FromHSL2(ColorHue + 7, ColorSat, 47 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + 21, ColorSat, 53 + ColorLightAdjust)})
                FrmMain.PanTitle.Background = Brush
            End If
            '标题栏主题图片
            If ThemeNow >= 5 AndAlso ThemeNow <> 14 Then
                FrmMain.ImgTitle.Source = $"{PathImage}Themes/{ThemeNow}.png"
            Else
                FrmMain.ImgTitle.Source = Nothing
            End If
            FrmMain.ImgTitle.Opacity = If(ThemeNow = 13, 0.25, 0.5)
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub

    ''' <summary>
    ''' 检查当前主题是否可用，并按解锁状态启用设置页里的主题按钮。
    ''' </summary>
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        RunInUi(
        Sub()
            Try
                Dim CurrentTheme As Integer = Settings.Get(Of Integer)("UiLauncherTheme")
                If Not ThemeCheckOne(CurrentTheme) Then
                    Logger.Warn($"检测到尚未解锁的主题：{CurrentTheme}，已重置为默认主题")
                    Settings.Set("UiLauncherTheme", 0)
                End If
                If Not EffectSetup OrElse FrmSetupUI Is Nothing OrElse FrmSetupUI.PanLauncherTheme Is Nothing Then Return
                '按主题 Id 启用或禁用对应单选框（RadioLauncherTheme5Gray 这类不含纯数字的不参与）
                For Each Control In FrmSetupUI.PanLauncherTheme.Children
                    If Not (TypeOf Control Is MyRadioBox) Then Continue For
                    Dim Box As MyRadioBox = CType(Control, MyRadioBox)
                    Dim NameMatch = System.Text.RegularExpressions.Regex.Match(Box.Name, "^RadioLauncherTheme(\d+)$")
                    If Not NameMatch.Success Then Continue For
                    Dim ThemeId As Integer = Val(NameMatch.Groups(1).Value)
                    Dim Unlocked As Boolean = ThemeCheckOne(ThemeId)
                    Box.IsEnabled = Unlocked
                    If Unlocked Then
                        '主题 8、9、11 上盖着官方的「未解锁」提示层，解锁后让它让出左键，否则会拦住点击。
                        '这些层上的右键/解密入口在按钮本身也绑定了同一个处理器，所以不受影响。
                        Select Case ThemeId
                            Case 8
                                FrmSetupUI.LabLauncherTheme8Copy.IsHitTestVisible = False
                            Case 9
                                FrmSetupUI.LabLauncherTheme9Copy.IsHitTestVisible = False
                            Case 11
                                FrmSetupUI.LabLauncherTheme11Click.IsHitTestVisible = False
                        End Select
                    Else
                        '未解锁（非主题版）时统一给出协议说明，替代官方的赞助解锁文案
                        Box.ToolTip = "为遵守 PCL 开源协议，我们不提供主题功能。"
                    End If
                Next
            Catch ex As Exception
                Logger.Error(ex, "检查主题失败", LogBehavior.AlertThenFeedback)
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 判断指定主题是否可用。基础主题始终可用；隐藏主题仅在 AllowHiddenThemes 为 True 时可用。
    ''' </summary>
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        If {0, 1, 2, 3, 4, 42}.Contains(Id) Then Return True
        Return AllowHiddenThemes
    End Function

    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        '与官方正式版一致：解锁由服务器公告写入 UiLauncherThemeHide2 完成，此处不额外处理
        Return False
    End Function
#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        '本分支已移除更新功能，这里不再提示
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region


#Region "AI 崩溃分析"

    ''' <summary>最多允许几轮工具交互（AI 每次请求文件算一轮）。</summary>
    Private Const AiMaxToolRounds As Integer = 4
    ''' <summary>单个文件最多读取多少字符。</summary>
    Private Const AiMaxReadLength As Integer = 4000

    ''' <summary>系统提示词：约束输出结构，并声明可用的工具箱。</summary>
    Public Const AiSystemPrompt As String = "你是一个 Minecraft 启动器（PCL）的崩溃排查助手，可以主动读取和修改本地文件来帮助用户解决问题。" &
        "用户会给你一份日志。请用简体中文回答，先给出分析，再给出可执行的修改建议。" & vbCrLf &
        "分析部分请按下面结构：" & vbCrLf &
        "1. 出错原因：一两句话点明最可能的直接原因。" & vbCrLf &
        "2. 关键证据：引用日志中最能说明问题的原文。" & vbCrLf &
        "3. 判断依据：说明你的推理过程。" & vbCrLf &
        "信息不足时必须明说，不要编造。" & vbCrLf & vbCrLf &
        "在回答的最后，必须附上一个 JSON 代码块，格式严格如下：" & vbCrLf &
        "```json" & vbCrLf &
        "{""actions"":[{""type"":""manual"",""desc"":""需要用户手动完成的事项""}]}" & vbCrLf &
        "```" & vbCrLf &
        "actions 支持的 type 如下（path 一律是相对游戏目录的路径）：" & vbCrLf &
        "read_file —— 请求读取某个文件。你会收到文件内容，然后可以继续给出进一步判断（最多 4 轮）。" & vbCrLf &
        "delete_file —— 删除文件。只允许删除日志或报错信息里确实出现过的文件。" & vbCrLf &
        "write_file —— 覆盖写入文件，需要 content 字段写完整的新内容。" & vbCrLf &
        "replace —— 在文件中做文本替换，需要 old 与 new 两个字段，old 必须与文件内容完全一致。" & vbCrLf &
        "open_folder —— 打开文件夹。" & vbCrLf &
        "manual —— 无法自动完成，desc 写明用户该做什么。" & vbCrLf &
        "严禁推测路径，只能依据日志或已读到的文件内容。执行前都会经过用户确认，若无可执行项请返回空数组。"

    ''' <summary>分析历史存放目录。</summary>
    Public ReadOnly Property AiHistoryFolder As String
        Get
            Return Paths.Base & "PCL\AiAnalysis\"
        End Get
    End Property

    ''' <summary>按消息数组发起一次对话。</summary>
    Public Function AiChatWithMessages(Messages As JArray) As String
        Dim ApiKey As String = Settings.Get(Of String)("AiAnalyzeApiKey")
        Dim Endpoint As String = Settings.Get(Of String)("AiAnalyzeApiUrl")
        Dim IsLocalEndpoint As Boolean = Endpoint.Contains("127.0.0.1") OrElse Endpoint.Contains("localhost")
        If String.IsNullOrWhiteSpace(ApiKey) AndAlso Not IsLocalEndpoint Then Throw New Exception("还没有填写 API Key，请在「AI 分析」页里配置。")
        Dim Url As String = Settings.Get(Of String)("AiAnalyzeApiUrl")
        If String.IsNullOrWhiteSpace(Url) Then Throw New Exception("接口地址为空。")
        Dim Model As String = Settings.Get(Of String)("AiAnalyzeModel")
        If String.IsNullOrWhiteSpace(Model) Then Model = "deepseek-chat"

        Dim Body As New JObject From {
            {"model", Model},
            {"messages", Messages},
            {"stream", False},
            {"temperature", 0.3}}
        Dim Headers(,) As String = {{"Authorization", "Bearer " & ApiKey}}
        Dim Response As String = NetRequestByClient(Url, HttpMethod.Post, Body.ToString(), "application/json",
                                                   Headers:=Headers, Timeout:=180000, RequireJson:=True, MakeLog:=False)
        Dim Json As JObject = Response.DeserializeJson()
        If Json("error") IsNot Nothing Then Throw New Exception(Json("error")("message").ToString())
        Dim Choices = Json("choices")
        If Choices Is Nothing OrElse Choices.Count = 0 Then Throw New Exception("接口没有返回有效内容：" & Response)
        Return Choices(0)("message")("content").ToString()
    End Function

    ''' <summary>
    ''' 流式对话：每收到一小段内容就回调一次 OnDelta。返回完整回答。
    ''' </summary>
    Public Function AiChatStream(Messages As JArray, OnDelta As Action(Of String)) As String
        Dim ApiKey As String = Settings.Get(Of String)("AiAnalyzeApiKey")
        Dim Endpoint As String = Settings.Get(Of String)("AiAnalyzeApiUrl")
        Dim IsLocalEndpoint As Boolean = Endpoint.Contains("127.0.0.1") OrElse Endpoint.Contains("localhost")
        If String.IsNullOrWhiteSpace(ApiKey) AndAlso Not IsLocalEndpoint Then Throw New Exception("还没有填写 API Key，请在「AI 分析」页里配置。")
        Dim Url As String = Settings.Get(Of String)("AiAnalyzeApiUrl")
        If String.IsNullOrWhiteSpace(Url) Then Throw New Exception("接口地址为空。")
        Dim Model As String = Settings.Get(Of String)("AiAnalyzeModel")
        If String.IsNullOrWhiteSpace(Model) Then Model = "deepseek-chat"

        Dim Body As New JObject From {
            {"model", Model},
            {"messages", Messages},
            {"stream", True},
            {"temperature", 0.3}}
        Dim Payload As Byte() = Text.Encoding.UTF8.GetBytes(Body.ToString())

        Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
        Req.Method = "POST"
        Req.ContentType = "application/json"
        Req.Accept = "text/event-stream"
        Req.Headers("Authorization") = "Bearer " & ApiKey
        Req.Timeout = 180000
        Req.ReadWriteTimeout = 180000
        Req.AllowReadStreamBuffering = False
        Req.UserAgent = "PCL2/" & VersionBaseName
        Req.ContentLength = Payload.Length
        Using RequestStream = Req.GetRequestStream()
            RequestStream.Write(Payload, 0, Payload.Length)
        End Using

        Logger.Info($"发起 AI 流式请求：{Url}（模型 {Model}，{Messages.Count} 条消息）")
        Dim Builder As New Text.StringBuilder
        Dim ChunkCount As Integer = 0
        Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
            Using Reader As New IO.StreamReader(Resp.GetResponseStream(), Text.Encoding.UTF8)
                Do While Not Reader.EndOfStream
                    Dim Line As String = Reader.ReadLine()
                    If Line Is Nothing Then Exit Do
                    If Not Line.StartsWithF("data:") Then Continue Do
                    Dim Chunk As String = Line.Substring(5).Trim()
                    If Chunk = "[DONE]" Then Exit Do
                    If Chunk = "" Then Continue Do
                    Try
                        Dim Json As JObject = Chunk.DeserializeJson()
                        If Json("error") IsNot Nothing Then Throw New Exception(Json("error")("message").ToString())
                        Dim Choices = Json("choices")
                        If Choices IsNot Nothing AndAlso Choices.Count > 0 Then
                            Dim Piece As String = If(Choices(0)("delta")?("content")?.ToString, "")
                            If Piece <> "" Then
                                Builder.Append(Piece)
                                ChunkCount += 1
                                OnDelta(Piece)
                            End If
                        End If
                    Catch ex As Exception
                        If TypeOf ex Is Exception AndAlso ex.Message.StartsWithF("接口返回错误") Then Throw
                    End Try
                Loop
            End Using
        End Using
                Logger.Info($"AI 流式请求完成：收到 {ChunkCount} 个分片，共 {Builder.Length} 字符")
        Return Builder.ToString()
    End Function

    ''' <summary>最基础的一问一答。</summary>
    Public Function AiChat(UserContent As String, Optional SystemPrompt As String = Nothing) As String
        Dim Messages As New JArray
        Messages.Add(New JObject From {{"role", "system"}, {"content", If(SystemPrompt, AiSystemPrompt)}})
        Messages.Add(New JObject From {{"role", "user"}, {"content", UserContent}})
        Return AiChatWithMessages(Messages)
    End Function

    ''' <summary>把日志裁剪到合理长度。</summary>
    Private Function AiTrimLog(LogContent As String) As String
        Const MaxLogLength As Integer = 16000
        If LogContent.Length <= MaxLogLength Then Return LogContent
        Dim HeadPart As String = LogContent.Substring(0, MaxLogLength \ 3)
        Dim TailPart As String = LogContent.Substring(LogContent.Length - MaxLogLength * 2 \ 3)
        Return HeadPart & vbCrLf & "……（中间省略 " & (LogContent.Length - MaxLogLength) & " 个字符）……" & vbCrLf & TailPart
    End Function

    ''' <summary>
    ''' 带工具能力的分析：AI 可以多次请求读取文件，读完后继续推理。
    ''' 返回最后一条回答（含 actions JSON）。
    ''' </summary>
    Public Function AiAnalyzeWithTools(LogContent As String, GameFolder As String,
                                       Optional OnProgress As Action(Of String) = Nothing,
                                       Optional OnDelta As Action(Of String) = Nothing) As String
        Dim Messages As New JArray
        Messages.Add(New JObject From {{"role", "system"}, {"content", AiSystemPrompt & vbCrLf & vbCrLf & AiPermissionNote(GameFolder)}})
        Messages.Add(New JObject From {{"role", "user"}, {"content", "请分析下面这份日志：" & vbCrLf & vbCrLf & AiTrimLog(LogContent)}})
        Dim Answer As String = ""
        For Round As Integer = 1 To AiMaxToolRounds
            If OnProgress IsNot Nothing Then OnProgress($"正在请求 AI 分析（第 {Round} 轮）……")
            If OnDelta IsNot Nothing Then
                Answer = AiChatStream(Messages, OnDelta)
            Else
                Answer = AiChatWithMessages(Messages)
            End If
            Dim Actions As JArray = AiParseActions(Answer)
            Dim ReadList As New List(Of JObject)
            For i = 0 To Actions.Count - 1
                Dim Act As JObject = CType(Actions(i), JObject)
                If If(Act("type")?.ToString, "") = "read_file" Then ReadList.Add(Act)
            Next
            If ReadList.Count = 0 Then Return Answer
            If OnProgress IsNot Nothing Then OnProgress($"AI 请求读取 {ReadList.Count} 个文件……")
            Messages.Add(New JObject From {{"role", "assistant"}, {"content", Answer}})
            Dim Report As New Text.StringBuilder
            Report.AppendLine("以下是你请求的文件内容：")
            For Each Req As JObject In ReadList
                Dim Rel As String = If(Req("path")?.ToString, "")
                Report.AppendLine($"=== {Rel} ===")
                Try
                    Dim Full As String = AiEnsureInside(GameFolder, IO.Path.Combine(GameFolder, Rel))
                    If IO.File.Exists(Full) Then
                        Dim Body2 As String = FileUtils.ReadAsString(Full)
                        If Body2.Length > AiMaxReadLength Then Body2 = Body2.Substring(0, AiMaxReadLength) & vbCrLf & "……（内容过长已截断）"
                        Report.AppendLine(Body2)
                    Else
                        Report.AppendLine("(文件不存在)")
                    End If
                Catch ex As Exception
                    Report.AppendLine($"(读取失败：{ex.Message})")
                End Try
            Next
            Messages.Add(New JObject From {{"role", "user"}, {"content", Report.ToString()}})
        Next
        Return Answer
    End Function

    ''' <summary>从 AI 回答中剥离 JSON 建议块，返回给用户看的正文。</summary>
    Public Function AiStripActions(Answer As String) As String
        Return Text.RegularExpressions.Regex.Replace(Answer, "```json[\s\S]*?```", "",
                                                     Text.RegularExpressions.RegexOptions.IgnoreCase).Trim()
    End Function

    ''' <summary>解析 AI 回答里的 JSON 建议块，失败时返回空数组。</summary>
    Public Function AiParseActions(Answer As String) As JArray
        Try
            Dim Matched = Text.RegularExpressions.Regex.Match(Answer, "```json\s*(\{[\s\S]*?\})\s*```",
                                                              Text.RegularExpressions.RegexOptions.IgnoreCase)
            If Not Matched.Success Then Return New JArray
            Dim Json As JObject = Matched.Groups(1).Value.DeserializeJson()
            If Json("actions") Is Nothing Then Return New JArray
            Return CType(Json("actions"), JArray)
        Catch ex As Exception
            Logger.Warn(ex, "解析 AI 修复建议失败")
            Return New JArray
        End Try
    End Function

    ''' <summary>把一次分析写入历史文件。</summary>
    Public Function AiSaveHistory(LogFileName As String, LogExcerpt As String, Answer As String) As String
        DirectoryUtils.Create(AiHistoryFolder)
        Dim Json As New JObject From {
            {"time", Date.Now.ToString("yyyy-MM-dd HH:mm:ss")},
            {"log_file", LogFileName},
            {"log_excerpt", LogExcerpt},
            {"answer", Answer}}
        Dim Target As String = AiHistoryFolder & Date.Now.ToString("yyyyMMdd_HHmmss") & ".json"
        FileUtils.Write(Target, Json.ToString())
        Logger.Info($"AI 分析结果已保存：{Target}")
        Return Target
    End Function

    ''' <summary>确保路径在 Root 内，防止越权访问。</summary>
    ''' <summary>
    ''' 按当前权限模式校验路径。
    ''' 0 = 只读：读取也限制在游戏目录内；1 = 可写：只能在游戏目录内改动；2 = 完全访问：不限制。
    ''' </summary>
    ''' <summary>
    ''' 按当前权限模式校验路径。
    ''' 0 = 只读 / 1 = 可写游戏目录（含额外允许目录）/ 2 = 完全访问。
    ''' </summary>
    Private Function AiEnsureInside(Root As String, Target As String) As String
        Dim Permission As Integer = Settings.Get(Of Integer)("AiPermission")
        Dim Full As String = IO.Path.GetFullPath(Target)
        If Permission >= 2 Then
            Logger.Warn($"AI 以完全访问权限操作：{Full}")
            Return Full
        End If
        '1) 游戏目录内
        If Not String.IsNullOrWhiteSpace(Root) Then
            Dim BasePath As String = IO.Path.GetFullPath(Root)
            If Not BasePath.EndsWith("\") Then BasePath &= "\"
            If Full.StartsWith(BasePath, StringComparison.OrdinalIgnoreCase) Then Return Full
        End If
        '2) 额外允许目录内
        For Each Line As String In AiExtraFolderList()
            Dim Extra As String = IO.Path.GetFullPath(Line)
            If Not Extra.EndsWith("\") Then Extra &= "\"
            If Full.StartsWith(Extra, StringComparison.OrdinalIgnoreCase) Then Return Full
        Next
        Throw New Exception("已阻止越权操作：目标不在可访问目录内。" & vbCrLf & Full & vbCrLf & vbCrLf &
                            "如需让 AI 操作该位置，可以：" & vbCrLf &
                            "· 在 设置 → 系统 → AI 崩溃分析 的「额外允许目录」里加上它；" & vbCrLf &
                            "· 或把文件修改权限改为「完全访问」（不限制路径）。")
    End Function

    ''' <summary>
    ''' 额外允许 AI 访问的目录。启动器自身所在目录始终允许（AI 需要读日志、配置来排查问题），
    ''' 用户还可以在设置里追加更多目录。
    ''' </summary>
    Private Function AiExtraFolderList() As List(Of String)
        Dim Result As New List(Of String)
        Result.Add(Paths.Base)
        Dim Raw As String = Settings.Get(Of String)("AiExtraFolders")
        If Not String.IsNullOrWhiteSpace(Raw) Then
            For Each Line As String In Raw.Split({ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
                Dim T As String = Line.Trim()
                If T <> "" AndAlso Not Result.Contains(T) Then Result.Add(T)
            Next
        End If
        Return Result
    End Function

    ''' <summary>按当前权限生成环境说明，附加到系统提示词。</summary>
    Public Function AiPermissionNote(Root As String) As String
        Dim Permission As Integer = Settings.Get(Of Integer)("AiPermission")
        Dim Extras As List(Of String) = AiExtraFolderList()
        Dim ExtraText As String = If(Extras.Count > 0, "额外允许访问：" & String.Join("；", Extras), "")
        Select Case Permission
            Case 0
                Return "【当前权限】只读。你只能查看文件，不能做任何修改。" &
                       If(Root <> "", "可访问目录：" & Root & "。" & ExtraText, ExtraText)
            Case 1
                Return "【当前权限】可写（受限）。" &
                       If(Root <> "", "可访问目录：" & Root & "。", "尚未选中游戏版本，游戏目录不可用。") & ExtraText &
                       " 访问其他路径会被拒绝，此时请在回答里说明需要用户怎么做，不要反复重试。"
            Case Else
                Return "【当前权限】完全访问，可以读写任意路径。"
        End Select
    End Function

    ''' <summary>修改文件前先备份一份，返回备份路径。</summary>
    Private Function AiBackupFile(GameFolder As String, FullPath As String) As String
        Try
            If Not IO.File.Exists(FullPath) Then Return Nothing
            Dim BackupDir As String = AiHistoryFolder & "backup\" & Date.Now.ToString("yyyyMMdd_HHmmss") & "\"
            DirectoryUtils.Create(BackupDir)
            Dim Rel As String = FullPath.Substring(IO.Path.GetFullPath(GameFolder).TrimEnd("\"c).Length).TrimStart("\"c)
            Dim Target As String = IO.Path.Combine(BackupDir, Rel.Replace("\", "_").Replace("/", "_"))
            IO.File.Copy(FullPath, Target, True)
            Logger.Info($"已备份 {FullPath} → {Target}")
            Return Target
        Catch ex As Exception
            Logger.Warn(ex, "备份文件失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 执行一条 AI 建议，返回结果描述。
    ''' 支持 delete_file / write_file / replace / open_folder，全部限制在游戏目录内。
    ''' </summary>
    Public Function AiRunAction(Action As JObject, GameFolder As String) As String
        Dim TypeName As String = If(Action("type")?.ToString, "")
        Dim RelPath As String = If(Action("path")?.ToString, "")
        Select Case TypeName
            Case "delete_file"
                If RelPath = "" Then Throw New Exception("建议里没有给出文件路径")
                Dim Full As String = AiEnsureInside(GameFolder, IO.Path.Combine(GameFolder, RelPath))
                If Not IO.File.Exists(Full) Then Throw New Exception("文件不存在：" & Full)
                AiBackupFile(GameFolder, Full)
                IO.File.Delete(Full)
                Return "已删除（原文件已备份到 AI 历史目录下的 backup 文件夹）"

            Case "write_file"
                If RelPath = "" Then Throw New Exception("建议里没有给出文件路径")
                Dim Content As String = If(Action("content")?.ToString, Nothing)
                If Content Is Nothing Then Throw New Exception("建议里没有给出 content")
                Dim Full As String = AiEnsureInside(GameFolder, IO.Path.Combine(GameFolder, RelPath))
                AiBackupFile(GameFolder, Full)
                DirectoryUtils.Create(IO.Path.GetDirectoryName(Full))
                FileUtils.Write(Full, Content)
                Return "已写入（原文件已备份）"

            Case "replace"
                If RelPath = "" Then Throw New Exception("建议里没有给出文件路径")
                Dim OldText As String = If(Action("old")?.ToString, Nothing)
                Dim NewText As String = If(Action("new")?.ToString, "")
                If OldText Is Nothing Then Throw New Exception("建议里没有给出 old 字段")
                Dim Full As String = AiEnsureInside(GameFolder, IO.Path.Combine(GameFolder, RelPath))
                If Not IO.File.Exists(Full) Then Throw New Exception("文件不存在：" & Full)
                Dim Body2 As String = FileUtils.ReadAsString(Full)
                If Not Body2.Contains(OldText) Then Throw New Exception("文件里找不到要替换的内容，可能已经被改过了")
                AiBackupFile(GameFolder, Full)
                FileUtils.Write(Full, Body2.Replace(OldText, NewText))
                Return "已替换（原文件已备份）"

            Case "open_folder"
                If RelPath = "" Then Throw New Exception("建议里没有给出文件夹路径")
                Dim Full As String = AiEnsureInside(GameFolder, IO.Path.Combine(GameFolder, RelPath))
                If Not IO.Directory.Exists(Full) Then Throw New Exception("文件夹不存在：" & Full)
                OpenExplorer(Full)
                Return "已打开 " & RelPath

            Case Else
                Throw New Exception("这条建议需要手动处理")
        End Select
    End Function

#End Region

End Module
