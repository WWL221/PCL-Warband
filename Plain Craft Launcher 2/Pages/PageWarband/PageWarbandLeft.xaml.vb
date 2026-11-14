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
        Dim selected = WarbandFindModule(WarbandGamePath, WarbandModule)
        If selected Is Nothing Then
            Hint("请先在右侧下拉框选择要启动的模块（当前记录的是「" & If(WarbandModule, "空") & "」）。", HintType.Red)
            Return
        End If
        Dim errorMessage = WarbandLaunch(WarbandGamePath, selected, WarbandSelectedRuntime, WarbandNoIntro, WarbandExtraArgs)
        If errorMessage <> "" Then Hint(errorMessage, HintType.Red) Else Hint("已启动：" & selected.Folder, HintType.Blue)
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
