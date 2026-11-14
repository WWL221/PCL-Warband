Imports System.IO

Public Class PageWarbandLeft

    Private Sub PageWarbandLeft_Loaded() Handles Me.Loaded
        RefreshWarbandLeft()
    End Sub

    Public Sub RefreshWarbandLeft()
        LabLeftModule.Text = If(String.IsNullOrWhiteSpace(WarbandModule), "（未选择）", WarbandModule)
        LabLeftRuntime.Text = RuntimeName(WarbandSelectedRuntime)
        LabLeftPath.Text = If(String.IsNullOrWhiteSpace(WarbandGamePath), "未设置", WarbandGamePath)
    End Sub

    Private Sub BtnLeftLaunch_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLeftLaunch.Click
        Dim modules = WarbandScanModules(WarbandGamePath)
        Dim selected = modules.FirstOrDefault(Function(m) m.Folder.Equals(WarbandModule, StringComparison.OrdinalIgnoreCase))
        Dim errorMessage = WarbandLaunch(WarbandGamePath, selected, WarbandSelectedRuntime, WarbandNoIntro, WarbandExtraArgs)
        If errorMessage <> "" Then Hint(errorMessage, HintType.Red)
    End Sub

    Private Sub BtnLeftOpenGame_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLeftOpenGame.Click
        If Directory.Exists(WarbandGamePath) Then
            Process.Start("explorer.exe", WarbandGamePath)
        Else
            Hint("还没有设置有效的游戏目录。")
        End If
    End Sub

    Private Sub BtnLeftOpenConfig_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLeftOpenConfig.Click
        Try
            Directory.CreateDirectory(WarbandConfigDir)
            Process.Start("explorer.exe", WarbandConfigDir)
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 打开配置目录失败")
        End Try
    End Sub

End Class
