Imports System.IO
Imports System.IO.Compression

''' <summary>Mod 介绍查看窗口：显示图标、元信息与完整的 Markdown 介绍，并支持离线翻译。</summary>
Public Class FrmModInfo

    Private ReadOnly Meta As McModMetadata
    Private ReadOnly Proj As JObject
    Private ReadOnly JarPath As String

    Private OriginalText As String = ""
    Private Translated As Boolean = False
    Private Busy As Boolean = False

    Public Sub New(Meta As McModMetadata, Proj As JObject, Optional JarPath As String = "")
        InitializeComponent()
        Me.Meta = Meta
        Me.Proj = Proj
        Me.JarPath = JarPath
        AddHandler Loaded, AddressOf FrmModInfo_Loaded
    End Sub

    ''' <summary>拖动标题栏移动窗口。</summary>
    Private Sub PanTitleBar_MouseLeftButtonDown(sender As Object, e As Windows.Input.MouseButtonEventArgs)
        If e.ChangedButton = Windows.Input.MouseButton.Left Then DragMove()
    End Sub

    Private Sub BtnCloseTop_Click() Handles BtnCloseTop.Click
        Close()
    End Sub

    Private Sub FrmModInfo_Loaded(sender As Object, e As RoutedEventArgs)
        Try
            LabName.Text = If(String.IsNullOrWhiteSpace(Meta.Name), "未知 Mod", Meta.Name)
            LabTitle.Text = "Mod 介绍 · " & LabName.Text
            Dim Parts As New List(Of String)
            If Not String.IsNullOrWhiteSpace(Meta.ModId) Then Parts.Add($"ID：{Meta.ModId}")
            If Not String.IsNullOrWhiteSpace(Meta.Version) Then Parts.Add($"版本：{Meta.Version}")
            If Not String.IsNullOrWhiteSpace(Meta.Authors) Then Parts.Add($"作者：{Meta.Authors}")
            If Parts.Count = 0 Then Parts.Add("未读取到元数据")
            LabMeta.Text = String.Join("　·　", Parts)

            '介绍：优先用 Modrinth 的完整描述（含图片），否则用 jar 内的简短描述
            If Proj IsNot Nothing AndAlso Proj("body") IsNot Nothing AndAlso Proj("body").ToString.Trim <> "" Then
                OriginalText = Proj("body").ToString
            Else
                OriginalText = Meta.Description
            End If

            If String.IsNullOrWhiteSpace(OriginalText) Then
                OriginalText = "（这个 Mod 没有提供介绍信息）" & vbCrLf & vbCrLf &
                               "可以试试「打开主页」到 Mod 发布页查看。"
            End If

            ModMarkdown.Render(OriginalText, PanContent.Document)
            BtnTranslate.IsEnabled = Not String.IsNullOrWhiteSpace(OriginalText)

            '主页按钮
            BtnHomepage.IsEnabled = Not String.IsNullOrWhiteSpace(HomepageUrl())
            LoadIcon()

            '按设置自动翻译
            If Settings.Get(Of Boolean)("ModInfoAutoTranslate") Then BtnTranslate_Click()
        Catch ex As Exception
            Logger.Error(ex, "显示 Mod 介绍失败")
        End Try
    End Sub

    ''' <summary>主页地址：优先用 Modrinth，其次 jar 内声明的。</summary>
    Private Function HomepageUrl() As String
        If Proj IsNot Nothing AndAlso Proj("project_url") IsNot Nothing Then Return Proj("project_url").ToString
        If Not String.IsNullOrWhiteSpace(Meta.Homepage) Then Return Meta.Homepage
        If Meta.ModId <> "" Then Return "https://modrinth.com/mod/" & Meta.ModId
        Return ""
    End Function

    ''' <summary>从 jar 里取出图标并显示。</summary>
    Private Sub LoadIcon()
        If String.IsNullOrWhiteSpace(Meta.IconEntry) OrElse Not FileUtils.Exists(JarPath) Then Return
        RunInNewThread(
        Sub()
            Dim Source As Windows.Media.ImageSource = Nothing
            Try
                Using Zip As ZipArchive = ZipFile.OpenRead(JarPath)
                    Dim Entry As ZipArchiveEntry = Zip.GetEntry(Meta.IconEntry)
                    If Entry IsNot Nothing Then
                        Dim Buffer As New IO.MemoryStream
                        Entry.Open().CopyTo(Buffer)
                        Buffer.Position = 0
                        Source = System.Windows.Media.Imaging.BitmapFrame.Create(Buffer, Windows.Media.Imaging.BitmapCreateOptions.None, Windows.Media.Imaging.BitmapCacheOption.OnLoad)
                    End If
                End Using
            Catch ex As Exception
                Logger.Warn(ex, "读取 Mod 图标失败")
            End Try
            If Source Is Nothing Then Return
            RunInUi(Sub() ImgIcon.Source = Source)
        End Sub, "读取 Mod 图标")
    End Sub

    ''' <summary>用本地翻译模型把介绍译成中文。</summary>
    Private Sub BtnTranslate_Click() Handles BtnTranslate.Click
        If Busy Then Return
        If Translated Then
            ModMarkdown.Render(OriginalText, PanContent.Document)
            Translated = False
            BtnTranslate.Text = "翻译成中文"
            BtnOriginal.Visibility = Visibility.Collapsed
            Return
        End If
        Busy = True
        BtnTranslate.IsEnabled = False
        BtnTranslate.Text = "正在翻译……"
        RunInNewThread(
        Sub()
            Dim Result As String = ""
            Dim Err As Exception = Nothing
            Try
                Result = ModTranslate.Translate(OriginalText)
            Catch ex As Exception
                Err = ex
            End Try
            RunInUi(
            Sub()
                Busy = False
                BtnTranslate.IsEnabled = True
                If Err IsNot Nothing Then
                    BtnTranslate.Text = "翻译成中文"
                    Hint($"翻译失败：{Err.Message}", HintType.Red)
                ElseIf String.IsNullOrWhiteSpace(Result) Then
                    BtnTranslate.Text = "翻译成中文"
                    Hint("本地模型没有返回内容", HintType.Red)
                Else
                    ModMarkdown.Render(Result, PanContent.Document)
                    Translated = True
                    BtnTranslate.Text = "翻译成中文"
                    BtnOriginal.Visibility = Visibility.Visible
                End If
            End Sub)
        End Sub, "翻译 Mod 介绍")
    End Sub

    Private Sub BtnOriginal_Click() Handles BtnOriginal.Click
        ModMarkdown.Render(OriginalText, PanContent.Document)
        Translated = False
        BtnOriginal.Visibility = Visibility.Collapsed
    End Sub

    Private Sub BtnHomepage_Click() Handles BtnHomepage.Click
        Dim Url As String = HomepageUrl()
        If Url <> "" Then OpenWebsite(Url)
    End Sub

    Private Sub BtnClose_Click() Handles BtnClose.Click
        Close()
    End Sub

End Class
