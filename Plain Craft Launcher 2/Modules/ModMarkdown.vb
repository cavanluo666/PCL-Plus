Imports System.Text.RegularExpressions
Imports System.Windows.Documents
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

''' <summary>
''' 把 Markdown 文本渲染成 FlowDocument。支持标题、粗体、行内代码、代码块、
''' 无序列表、分隔线与图片（图片会异步下载后插入）。
''' </summary>
Public Module ModMarkdown

    ''' <summary>渲染 Markdown 到指定文档。图片异步加载，加载完成后自动插入。</summary>
    Public Sub Render(Text As String, Doc As FlowDocument)
        Try
            Doc.Blocks.Clear()
        Catch ex As Exception
            'RichTextBox 的撤销栈偶发异常时，清空会失败；这里忽略，后续重建即可
            Logger.Warn(ex, "清空文档内容失败")
        End Try
        Doc.FontFamily = New FontFamily("Microsoft YaHei UI")
        Doc.FontSize = 12.5
        Doc.LineHeight = 19
        Doc.PagePadding = New Thickness(0)
        If String.IsNullOrEmpty(Text) Then Return
        'Modrinth 的介绍里夹着 HTML（<details>、<br> 等），先清理掉
        Text = Regex.Replace(Text, "<br\s*/?>", vbCrLf, RegexOptions.IgnoreCase)
        Text = Regex.Replace(Text, "</?(details|summary|div|p|span|center|b|i|u|strong|em|table|tr|td|th|ul|ol|li|h[1-6])[^>]*>", "", RegexOptions.IgnoreCase)

        Dim Lines As String() = Text.Replace(vbCrLf, vbLf).Replace(ChrW(13), vbLf).Split(ChrW(10))
        Dim InCode As Boolean = False
        Dim CodeBuf As New Text.StringBuilder
        For Each RawLine As String In Lines
            Dim Line As String = RawLine.TrimEnd()
            Dim Trimmed As String = Line.TrimStart()
            If Trimmed.StartsWith("```") Then
                If InCode Then
                    Doc.Blocks.Add(BuildCodeBlock(CodeBuf.ToString()))
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
            If Trimmed = "" Then Continue For
            '分隔线
            If Trimmed = "---" OrElse Trimmed = "***" OrElse Trimmed = "___" Then
                Doc.Blocks.Add(New Paragraph With {.BorderBrush = New SolidColorBrush(Color.FromRgb(221, 221, 221)),
                                                   .BorderThickness = New Thickness(0, 1, 0, 0),
                                                   .Margin = New Thickness(0, 6, 0, 6)})
                Continue For
            End If
            '标题
            If Trimmed.StartsWith("#### ") Then
                Doc.Blocks.Add(BuildHeading(Trimmed.Substring(5), 13))
            ElseIf Trimmed.StartsWith("### ") Then
                Doc.Blocks.Add(BuildHeading(Trimmed.Substring(4), 14))
            ElseIf Trimmed.StartsWith("## ") Then
                Doc.Blocks.Add(BuildHeading(Trimmed.Substring(3), 15.5))
            ElseIf Trimmed.StartsWith("# ") Then
                Doc.Blocks.Add(BuildHeading(Trimmed.Substring(2), 17.5))
            ElseIf Trimmed.StartsWith("- ") OrElse Trimmed.StartsWith("* ") OrElse Trimmed.StartsWith("+ ") Then
                Dim Item As Paragraph = BuildParagraph(Trimmed.Substring(2))
                Item.Margin = New Thickness(14, 0, 0, 5)
                If Item.Inlines.FirstInline IsNot Nothing Then Item.Inlines.InsertBefore(Item.Inlines.FirstInline, New Run("· "))
                Doc.Blocks.Add(Item)
            ElseIf Regex.IsMatch(Trimmed, "^\d+[.)]\s") Then
                '有序列表
                Dim NumEnd As Integer = Trimmed.IndexOfAny(New Char() {"."c, ")"c})
                Dim NumText As String = Trimmed.Substring(0, NumEnd)
                Dim Body As String = Trimmed.Substring(NumEnd + 1).TrimStart()
                Dim Item As Paragraph = BuildParagraph(Body)
                Item.Margin = New Thickness(14, 0, 0, 5)
                If Item.Inlines.FirstInline IsNot Nothing Then Item.Inlines.InsertBefore(Item.Inlines.FirstInline, New Run(NumText & ". "))
                Doc.Blocks.Add(Item)
            ElseIf Trimmed.StartsWith("> ") OrElse Trimmed = ">" Then
                '引用
                Dim Quote As Paragraph = BuildParagraph(Trimmed.Substring(1).TrimStart())
                Quote.Margin = New Thickness(14, 2, 0, 6)
                Quote.Padding = New Thickness(10, 2, 0, 2)
                Quote.BorderBrush = New SolidColorBrush(Color.FromRgb(200, 200, 200))
                Quote.BorderThickness = New Thickness(3, 0, 0, 0)
                Doc.Blocks.Add(Quote)
            Else
                Doc.Blocks.Add(BuildParagraph(Line))
            End If
        Next
        If InCode AndAlso CodeBuf.Length > 0 Then Doc.Blocks.Add(BuildCodeBlock(CodeBuf.ToString()))
    End Sub

    Private Function BuildHeading(Text As String, Size As Double) As Paragraph
        Return New Paragraph(New Run(Text)) With {
            .FontSize = Size,
            .FontWeight = FontWeights.Bold,
            .Margin = New Thickness(0, 10, 0, 5)}
    End Function

    Private Function BuildCodeBlock(Code As String) As Paragraph
        Return New Paragraph(New Run(Code.TrimEnd())) With {
            .FontFamily = New FontFamily("Consolas"),
            .FontSize = 11.5,
            .Background = New SolidColorBrush(Color.FromRgb(238, 238, 238)),
            .Padding = New Thickness(8, 6, 8, 6),
            .Margin = New Thickness(0, 4, 0, 8)}
    End Function

    ''' <summary>构造段落，处理行内的 **粗体**、`代码` 与 ![图片](链接)。</summary>
    Private Function BuildParagraph(Text As String) As Paragraph
        Dim P As New Paragraph With {.Margin = New Thickness(0, 0, 0, 6)}
        Dim Buf As New Text.StringBuilder
        Dim i As Integer = 0
        Do While i < Text.Length
            '图片
            If Text(i) = "!"c AndAlso i + 1 < Text.Length AndAlso Text(i + 1) = "["c Then
                Dim CloseBracket As Integer = Text.IndexOf("]"c, i + 2)
                If CloseBracket > 0 AndAlso CloseBracket + 1 < Text.Length AndAlso Text(CloseBracket + 1) = "("c Then
                    Dim CloseParen As Integer = Text.IndexOf(")"c, CloseBracket + 2)
                    If CloseParen > CloseBracket Then
                        If Buf.Length > 0 Then
                            P.Inlines.Add(New Run(Buf.ToString()))
                            Buf.Clear()
                        End If
                        AddImage(P, Text.Substring(CloseBracket + 2, CloseParen - CloseBracket - 2).Trim)
                        i = CloseParen + 1
                        Continue Do
                    End If
                End If
            End If
            '链接 [文字](地址)
            If Text(i) = "["c AndAlso (i = 0 OrElse Text(i - 1) <> "!"c) Then
                Dim CloseBracket As Integer = Text.IndexOf("]"c, i + 1)
                If CloseBracket > i AndAlso CloseBracket + 1 < Text.Length AndAlso Text(CloseBracket + 1) = "("c Then
                    Dim CloseParen As Integer = Text.IndexOf(")"c, CloseBracket + 2)
                    If CloseParen > CloseBracket Then
                        If Buf.Length > 0 Then
                            P.Inlines.Add(New Run(Buf.ToString()))
                            Buf.Clear()
                        End If
                        Dim LinkText As String = Text.Substring(i + 1, CloseBracket - i - 1)
                        Dim LinkUrl As String = Text.Substring(CloseBracket + 2, CloseParen - CloseBracket - 2).Trim
                        Dim Link As New Hyperlink(New Run(LinkText))
                        If LinkUrl <> "" Then
                            Link.NavigateUri = New Uri(LinkUrl.Replace(" ", "%20"))
                            Link.ToolTip = LinkUrl
                            AddHandler Link.RequestNavigate, Sub(Sender As Object, Ev As Windows.Navigation.RequestNavigateEventArgs)
                                                                 OpenWebsite(Ev.Uri.AbsoluteUri)
                                                                 Ev.Handled = True
                                                             End Sub
                        End If
                        P.Inlines.Add(Link)
                        i = CloseParen + 1
                        Continue Do
                    End If
                End If
            End If
            '粗体
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
            '行内代码
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

    ''' <summary>插入一张图片：先放占位，下载完成后替换。</summary>
    Private Sub AddImage(P As Paragraph, Url As String)
        If String.IsNullOrWhiteSpace(Url) Then Return
        Dim Holder As New Border With {
            .Margin = New Thickness(0, 4, 0, 8),
            .Child = New TextBlock With {
                .Text = "正在加载图片……",
                .FontSize = 11.5,
                .Foreground = New SolidColorBrush(Color.FromRgb(150, 150, 150))}}
        P.Inlines.Add(New InlineUIContainer(Holder))
        RunInNewThread(
        Sub()
            Dim Source As ImageSource = Nothing
            Try
                Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
                Req.Timeout = 15000
                Req.UserAgent = "Mozilla/5.0"
                Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                    Dim Buffer As New IO.MemoryStream
                    Resp.GetResponseStream().CopyTo(Buffer)
                    Buffer.Position = 0
                    Dim Frame As BitmapFrame = BitmapFrame.Create(Buffer, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                    Source = Frame
                End Using
            Catch ex As Exception
                Logger.Warn(ex, $"加载介绍图片失败：{Url}")
            End Try
            RunInUi(
            Sub()
                Try
                    If Source Is Nothing Then
                        Holder.Child = New TextBlock With {
                            .Text = "（图片加载失败）",
                            .FontSize = 11.5,
                            .Foreground = New SolidColorBrush(Color.FromRgb(180, 180, 180))}
                    Else
                        Holder.Child = New Image With {.Source = Source, .MaxHeight = 480, .Stretch = Stretch.Uniform, .HorizontalAlignment = HorizontalAlignment.Left}
                    End If
                Catch
                End Try
            End Sub)
        End Sub, "加载介绍图片")
    End Sub

End Module
