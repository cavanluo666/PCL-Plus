Public Class PageOtherAbout

    Private Shadows IsLoaded As Boolean = False
    Private Sub PageOtherAbout_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        If IsLoaded Then Return
        IsLoaded = True

        '%VERSIONCODE% 原本显示 PCL 内部版本号（415），这里改为展示本分支跟进的上游主线版本；
        '%BRANCH% 原本显示 BuildType 的数值（Release=50），改为显示分支名，更有辨识度。
        ItemAboutPcl.Info = ItemAboutPcl.Info.Replace("%VERSION%", VersionDisplay).Replace("%VERSIONCODE%", VersionUpstream).Replace("%BRANCH%", VersionBranchMain)
        If BuildType = BuildTypes.Debug Then
            BtnDonateDonate.Visibility = Visibility.Collapsed
            BtnDonateOutput.Visibility = Visibility.Visible
        End If

    End Sub

    Public Shared Sub CopyIdentify() Handles BtnDonateCopy.Click
        ClipboardSet(Identify)
    End Sub
    Private Sub BtnDonateCodeInput_Click() Handles BtnDonateInput.Click
        InputPotatoCode(False)
    End Sub
    Private Sub BtnDonateOutput_Click(sender As Object, e As EventArgs) Handles BtnDonateOutput.Click
        GeneratePotatoCode()
    End Sub
    Private Sub BtnAboutUpdate_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnAboutUpdate.Click
        UpdateCheckByButton()
    End Sub

End Class
