Imports System.IO

Public Class PageWarbandLeft

    Private Sub PageWarbandLeft_Loaded() Handles Me.Loaded
        RefreshWarbandLeft()
    End Sub

    Public Sub RefreshWarbandLeft()
        LabLeftModule.Text = If(String.IsNullOrWhiteSpace(WarbandModule), "（未选择）", WarbandModule)
        LabLeftRuntime.Text = RuntimeName(WarbandSelectedRuntime)
        LabLeftPath.Text = If(String.IsNullOrWhiteSpace(WarbandGamePath), "未设置", WarbandGamePath)
        RefreshModuleArt()
    End Sub

    ' ---------------- 模块预览图 ----------------

    Private Sub RefreshModuleArt()
        Try
            Dim artPath = WarbandFindModuleArt(WarbandModule, WarbandGamePath)
            If artPath <> "" Then
                ImgModuleArt.ActualSource = artPath
                ImgModuleArt.Visibility = Visibility.Visible
                PanArtEmpty.Visibility = Visibility.Collapsed
            Else
                ImgModuleArt.Visibility = Visibility.Collapsed
                PanArtEmpty.Visibility = Visibility.Visible
            End If
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 加载模块预览图失败")
            ImgModuleArt.Visibility = Visibility.Collapsed
            PanArtEmpty.Visibility = Visibility.Visible
        End Try
    End Sub

    Private Sub BtnChangeArt_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnChangeArt.Click
        If String.IsNullOrWhiteSpace(WarbandModule) Then
            Hint("先选一个模块，再换图。", HintType.Red)
            Return
        End If
        Dim dialog As New Microsoft.Win32.OpenFileDialog With {
            .Title = "选择模块预览图（png / jpg / webp / bmp）",
            .Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*"
        }
        If dialog.ShowDialog() <> True Then Return
        Hint(WarbandSetModuleArt(WarbandModule, dialog.FileName), HintType.Blue)
        RefreshModuleArt()
    End Sub

    Private Sub BtnClearArt_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnClearArt.Click
        WarbandClearModuleArt(WarbandModule)
        Hint("已清除自定义图，回落到模块自带的 main.bmp", HintType.Blue)
        RefreshModuleArt()
    End Sub

    ' ---------------- 快捷按钮 ----------------

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
