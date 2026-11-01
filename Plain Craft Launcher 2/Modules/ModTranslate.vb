''' <summary>
''' Mod 介绍翻译调度。可选在线 AI（质量好、快）或本地模型（完全离线）。
''' 实测本地小模型（Haidass-143M）对 Mod 介绍这类文本极不稳定，所以默认走在线；
''' 在线失败时自动回退到本地模型。
''' </summary>
Public Module ModTranslate

    ''' <summary>按设置里的引擎翻译整段文本，返回中文。</summary>
    Public Function Translate(Text As String, Optional OnProgress As Action(Of Integer, Integer) = Nothing) As String
        If String.IsNullOrWhiteSpace(Text) Then Return ""
        Dim Engine As Integer = Settings.Get(Of Integer)("ModTranslateEngine")
        '2 = Google 翻译（最快，质量好，保留 Markdown）
        If Engine = 2 Then
            Try
                Return TranslateGoogle(Text, OnProgress)
            Catch ex As Exception
                Logger.Warn(ex, "Google 翻译失败，回退到在线 AI")
            End Try
        End If
        '1 = 在线 AI；2 失败时也走到这里
        If Engine = 1 OrElse Engine = 2 Then
            Try
                Return TranslateOnline(Text, OnProgress)
            Catch ex As Exception
                Logger.Warn(ex, "在线 AI 翻译失败，回退到本地模型")
            End Try
        End If
        Return ModLocalTranslate.TranslateToChinese(Text, OnProgress)
    End Function

    ''' <summary>Google 翻译：逐行分批提交，进度按批回报。</summary>
    Private Function TranslateGoogle(Text As String, OnProgress As Action(Of Integer, Integer)) As String
        If OnProgress IsNot Nothing Then OnProgress(0, 1)
        Dim Result As String = ModGoogleTranslate.Translate(Text)
        If OnProgress IsNot Nothing Then OnProgress(1, 1)
        Return Result
    End Function

    ''' <summary>在线翻译：按长度切块后交给已配置的 AI 接口，要求保留 Markdown 结构。</summary>
    Private Function TranslateOnline(Text As String, OnProgress As Action(Of Integer, Integer)) As String
        Dim Chunks As List(Of String) = SplitChunks(Text)
        If Chunks.Count = 0 Then Return ""
        Const Prompt As String = "You are a professional translator. Translate the user's text into Simplified Chinese. " &
                                 "Keep all Markdown formatting (headings, lists, links, images, code blocks) exactly as it is. " &
                                 "Output only the translation, with no explanations."
        Dim Result As New Text.StringBuilder
        For i = 0 To Chunks.Count - 1
            Try
                Dim Piece As String = AiChat(Chunks(i), Prompt)
                If Not String.IsNullOrWhiteSpace(Piece) Then
                    If Result.Length > 0 Then Result.AppendLine().AppendLine()
                    Result.Append(Piece.Trim())
                End If
            Catch ex As Exception
                Logger.Warn(ex, $"在线翻译第 {i + 1} 块失败，保留原文")
                If Result.Length > 0 Then Result.AppendLine().AppendLine()
                Result.Append(Chunks(i))
            End Try
            If OnProgress IsNot Nothing Then OnProgress(i + 1, Chunks.Count)
        Next
        Return Result.ToString()
    End Function

    ''' <summary>按长度切块，尽量不破坏 Markdown 段落。</summary>
    Private Function SplitChunks(Text As String) As List(Of String)
        Const MaxLength As Integer = 1500
        Dim Result As New List(Of String)
        Dim Buf As New Text.StringBuilder
        For Each Line As String In Text.Replace(vbCrLf, vbLf).Replace(ChrW(13), vbLf).Split(ChrW(10))
            If Buf.Length > 0 AndAlso Buf.Length + Line.Length + 1 > MaxLength Then
                Result.Add(Buf.ToString())
                Buf.Clear()
            End If
            If Buf.Length > 0 Then Buf.Append(vbLf)
            Buf.Append(Line)
        Next
        If Buf.Length > 0 Then Result.Add(Buf.ToString())
        Return Result
    End Function

End Module
