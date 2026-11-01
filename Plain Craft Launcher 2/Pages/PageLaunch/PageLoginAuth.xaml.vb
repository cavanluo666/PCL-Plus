Public Class PageLoginAuth
    Private IsFirstLoad As Boolean = True

    ''' <summary>内置的常用外置登录服务器（均已实测可用）。</summary>
    Private Shared ReadOnly PresetServers As String() = {
        "https://littleskin.cn/api/yggdrasil",
        "https://skin.mslmc.net/api/yggdrasil"
    }

    ''' <summary>把预设与历史记录填进服务器下拉框。</summary>
    Private Sub FillServerCombo()
        Dim Items As New List(Of String)
        Items.AddRange(PresetServers)
        Dim Saved As String = CurrentServerSetting()
        If Saved <> "" AndAlso Not Items.Contains(Saved) Then Items.Insert(0, Saved)
        ComboServer.ItemsSource = Items
    End Sub

    ''' <summary>读取当前应当生效的服务器地址（按版本优先，其次全局）。</summary>
    Private Shared Function CurrentServerSetting() As String
        If McInstanceSelected IsNot Nothing Then
            Dim PerVersion As String = Settings.Get(Of String)("VersionServerAuthServer", Instance:=McInstanceSelected)
            If Not String.IsNullOrWhiteSpace(PerVersion) Then Return PerVersion
        End If
        Return Settings.Get(Of String)("CacheAuthServerServer")
    End Function

    ''' <summary>把服务器地址写进设置（全局与当前版本），并推导注册地址。</summary>
    Private Shared Sub SaveServer(Address As String)
        Address = Address.Replace("¨", "").Trim().TrimEnd("/"c)
        Settings.Set("CacheAuthServerServer", Address)
        If McInstanceSelected IsNot Nothing Then Settings.Set("VersionServerAuthServer", Address, Instance:=McInstanceSelected)
        '从 Yggdrasil 地址推导出注册页地址，供「注册账号」按钮使用
        Dim Register As String = ""
        If Address <> "" Then
            Register = Address.Replace("/api/yggdrasil", "").Replace("/api/yggdrasil/", "") & "/auth/register"
        End If
        Settings.Set("CacheAuthServerRegister", Register)
        If McInstanceSelected IsNot Nothing Then Settings.Set("VersionServerAuthRegister", Register, Instance:=McInstanceSelected)
    End Sub

    ''' <summary>
    ''' 刷新页面显示的所有信息。
    ''' </summary>
    Public Sub Reload(KeepInput As Boolean)
        AniControlEnabled += 1
        Try
            '记住密码
            CheckRemember.Checked = Settings.Get(Of Boolean)("LoginRemember")
            '服务器列表
            Dim ServerInput As String = If(ComboServer Is Nothing, "", ComboServer.Text)
            FillServerCombo()
            If KeepInput AndAlso Not IsFirstLoad AndAlso ServerInput <> "" Then
                ComboServer.Text = ServerInput
            Else
                Dim Saved As String = CurrentServerSetting()
                ComboServer.Text = If(Saved <> "", Saved, If(PresetServers.Length > 0, PresetServers(0), ""))
            End If
            '账号
            If KeepInput AndAlso Not IsFirstLoad Then
                Dim Input As String = ComboName.Text
                ComboName.ItemsSource = If(Settings.Get(Of String)("LoginAuthEmail") = "", Nothing, Settings.Get(Of String)("LoginAuthEmail").Split("¨"))
                ComboName.Text = Input
            Else
                If Settings.Get(Of String)("LoginAuthEmail") = "" Then
                    ComboName.ItemsSource = Nothing
                Else
                    ComboName.ItemsSource = Settings.Get(Of String)("LoginAuthEmail").Split("¨")
                    ComboName.Text = Settings.Get(Of String)("LoginAuthEmail").BeforeFirst("¨")
                    If Settings.Get(Of Boolean)("LoginRemember") Then TextPass.Password = Settings.Get(Of String)("LoginAuthPass").BeforeFirst("¨").Trim
                End If
            End If
        Finally
            AniControlEnabled -= 1
        End Try
        IsFirstLoad = False
    End Sub

    ''' <summary>
    ''' 获取当前页面的登录信息。
    ''' </summary>
    Public Shared Function GetLoginData() As McLoginServer
        Dim Base As String = CurrentServerSetting()
        If FrmLoginAuth IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(FrmLoginAuth.ComboServer.Text) Then
            Base = FrmLoginAuth.ComboServer.Text.Trim()
        End If
        Base = Base.TrimEnd("/"c)
        Dim Server As String = Base & "/authserver"
        If FrmLoginAuth Is Nothing Then
            Return New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Server, .UserName = "", .Password = "", .Description = "Authlib-Injector", .Type = McLoginType.Auth}
        Else
            Return New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Server, .UserName = FrmLoginAuth.ComboName.Text.Replace("¨", "").Trim, .Password = FrmLoginAuth.TextPass.Password.Replace("¨", "").Trim, .Description = "Authlib-Injector", .Type = McLoginType.Auth}
        End If
    End Function

    ''' <summary>
    ''' 当前页面的登录信息是否有效。
    ''' </summary>
    Public Shared Function IsVaild(LoginData As McLoginServer) As String
        If LoginData.BaseUrl = "/authserver" Then Return "服务器地址不能为空！"
        If LoginData.UserName = "" Then Return "账号不能为空！"
        If LoginData.Password = "" Then Return "密码不能为空！"
        Return ""
    End Function
    Public Function IsVaild() As String
        Return IsVaild(GetLoginData())
    End Function

    '服务器地址变化时保存
    Private Sub ComboServer_TextChanged(sender As Object, e As TextChangedEventArgs) Handles ComboServer.TextChanged
        If AniControlEnabled > 0 Then Return
        SaveServer(ComboServer.Text)
        Settings.Set("CacheAuthAccess", "")  '迫使其不进行 Validate
    End Sub
    Private Sub ComboServer_SelectionChanged(sender As MyComboBox, e As SelectionChangedEventArgs) Handles ComboServer.SelectionChanged
        If AniControlEnabled > 0 Then Return
        If sender.SelectedItem Is Nothing Then Return
        SaveServer(sender.SelectedItem.ToString)
        Settings.Set("CacheAuthAccess", "")
    End Sub

    '保存输入信息
    Private Sub ComboName_TextChanged(sender As Object, e As TextChangedEventArgs) Handles ComboName.TextChanged
        If sender.Text = "" Then TextPass.Password = ""
        If AniControlEnabled = 0 Then Settings.Set("CacheAuthAccess", "")  '迫使其不进行 Validate
    End Sub
    Private Sub TextPass_PasswordChanged(sender As Object, e As RoutedEventArgs) Handles TextPass.PasswordChanged
        If AniControlEnabled = 0 Then Settings.Set("CacheAuthAccess", "")
    End Sub
    Private Sub ComboName_SelectionChanged(sender As MyComboBox, e As SelectionChangedEventArgs) Handles ComboName.SelectionChanged
        If sender.SelectedIndex = -1 OrElse Not Settings.Get(Of Boolean)("LoginRemember") Then
            TextPass.Password = ""
        Else
            TextPass.Password = Settings.Get(Of String)("LoginAuthPass").ToString.Split("¨")(sender.SelectedIndex).Trim
        End If
    End Sub
    Private Sub CheckBoxChange(sender As MyCheckBox, e As Object) Handles CheckRemember.Change
        If AniControlEnabled = 0 Then Settings.Set(sender.Tag, sender.Checked)
    End Sub

    '链接处理
    Private Sub ComboName_TextChanged() Handles ComboName.TextChanged
        BtnLink.Content = If(ComboName.Text = "", "注册账号", "找回密码")
    End Sub
    Private Sub Btn_Click(sender As Object, e As EventArgs) Handles BtnLink.Click
        Dim Website As String = If(McInstanceSelected IsNot Nothing, Settings.Get(Of String)("VersionServerAuthRegister", Instance:=McInstanceSelected), Settings.Get(Of String)("CacheAuthServerRegister"))
        If BtnLink.Content = "注册账号" Then
            OpenWebsite(Website)
        Else
            OpenWebsite(Website.Replace("/auth/register", "/auth/forgot"))
        End If
    End Sub
    '切换注册按钮可见性
    Private Sub ReloadRegisterButton() Handles Me.Loaded
        Dim Address As String = If(McInstanceSelected IsNot Nothing, Settings.Get(Of String)("VersionServerAuthRegister", Instance:=McInstanceSelected), Settings.Get(Of String)("CacheAuthServerRegister"))
        BtnLink.Visibility = If(String.IsNullOrEmpty(New ValidateHttp().Validate(Address)), Visibility.Visible, Visibility.Collapsed)
    End Sub

End Class
