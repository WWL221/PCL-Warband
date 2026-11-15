Imports System.Windows.Threading
Imports System.IO

Public Class PageWarbandDownload

    Private Class TaskRow
        Public Task As WarbandDownloadTask
        Public Track As Border
        Public Fill As Border
        Public LabTitle As TextBlock
        Public LabInfo As TextBlock
        Public BtnPause As MyButton
        Public BtnCancel As MyButton
        Public BtnOpen As MyButton
    End Class

    Private ReadOnly Rows As New List(Of TaskRow)
    Private ReadOnly AllTasks As New List(Of WarbandDownloadTask)
    Private UiTimer As DispatcherTimer
    Private IsWarbandDownloadLoaded As Boolean

    Private ReadOnly NetdiskDomains As String() = {"pan.baidu.com", "123pan.com", "123684.com", "pan.quark.cn", "aliyundrive.com", "alipan.com", "lanzou", "cloud.189.cn", "caiyun.139.com"}

    ' ---------------- 生命周期 ----------------

    Private Sub PageWarbandDownload_Loaded() Handles Me.Loaded
        If IsWarbandDownloadLoaded Then Return
        IsWarbandDownloadLoaded = True

        If TextSaveDir.Text.Trim() = "" Then TextSaveDir.Text = DefaultModuleDir()

        UiTimer = New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(600)}
        AddHandler UiTimer.Tick, AddressOf RefreshTaskRows
        UiTimer.Start()
    End Sub

    Private Function DefaultModuleDir() As String
        If Not String.IsNullOrWhiteSpace(WarbandGamePath) Then
            Return Path.Combine(WarbandGamePath, "Modules")
        End If
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCL Warband Downloads")
    End Function

    ' ---------------- 新建下载 ----------------

    Private Sub TextUrl_TextChanged(sender As Object, e As TextChangedEventArgs) Handles TextUrl.TextChanged
        If LabStartHint Is Nothing Then Return

        Dim url = TextUrl.Text.Trim()
        If url = "" Then
            LabStartHint.Text = ""
            Return
        End If

        Dim netdisk = WarbandNetdiskName(url)
        If netdisk <> "" AndAlso (url.ToLowerInvariant().EndsWith("/") OrElse Not url.Contains("?") OrElse url.Contains("/s/")) Then
            Dim looksLikeShare = NetdiskDomains.Any(Function(domain) url.ToLowerInvariant().Contains(domain) AndAlso url.ToLowerInvariant().Contains("/s/"))
            If looksLikeShare Then
                LabStartHint.Text = $"这是 {netdisk} 的分享页，不是文件直链。请先转成直链再粘贴（或用客户端下好后手动放入 Modules）。"
                Return
            End If
        End If

        If TextFileName.Text.Trim() = "" Then
            Dim guessed = GuessFileName(url)
            If guessed <> "" Then TextFileName.Text = guessed
        End If
        LabStartHint.Text = ""
    End Sub

    Private Shared Function GuessFileName(url As String) As String
        Try
            Dim clean = url.Split("?"c)(0).Split("#"c)(0)
            Dim candidate = clean.Substring(clean.LastIndexOf("/"c) + 1)
            candidate = Uri.UnescapeDataString(candidate)
            If candidate.Contains(".") AndAlso candidate.Length <= 120 Then Return candidate
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 解析文件名失败")
        End Try
        Return ""
    End Function

    Private Sub BtnPaste_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPaste.Click
        Try
            If Clipboard.ContainsText() Then
                TextUrl.Text = Clipboard.GetText().Trim()
            End If
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 读取剪贴板失败")
        End Try
    End Sub

    Private Sub BtnBrowseDir_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnBrowseDir.Click
        Dim dialog As New Ookii.Dialogs.Wpf.VistaFolderBrowserDialog With {
            .Description = "选择下载保存目录",
            .UseDescriptionForTitle = True
        }
        If Directory.Exists(TextSaveDir.Text) Then dialog.SelectedPath = TextSaveDir.Text
        If dialog.ShowDialog() = True Then TextSaveDir.Text = dialog.SelectedPath
    End Sub

    Private Sub BtnDefaultDir_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnDefaultDir.Click
        TextSaveDir.Text = DefaultModuleDir()
    End Sub

    Private Sub BtnStart_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnStart.Click
        Dim url = TextUrl.Text.Trim()
        If url = "" Then
            LabStartHint.Text = "先粘贴一个下载链接。"
            Return
        End If
        If Not url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) AndAlso Not url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
            LabStartHint.Text = "只支持 http/https 链接。"
            Return
        End If

        Dim folder = TextSaveDir.Text.Trim()
        If folder = "" Then folder = DefaultModuleDir()
        Try
            Directory.CreateDirectory(folder)
        Catch ex As Exception
            LabStartHint.Text = "保存目录不可用：" & ex.Message
            Return
        End Try

        Dim fileName = TextFileName.Text.Trim()
        If fileName = "" Then fileName = GuessFileName(url)
        If fileName = "" Then fileName = "download-" & Date.Now.ToString("yyyyMMdd-HHmmss")
        fileName = fileName.Replace("\"c, "_").Replace("/"c, "_").Replace(":"c, "_")

        Dim savePath = Path.Combine(folder, fileName)
        Dim threads = 8
        Dim item = TryCast(ComboThreads.SelectedItem, MyComboBoxItem)
        If item IsNot Nothing AndAlso item.Tag IsNot Nothing Then Integer.TryParse(item.Tag.ToString(), threads)
        If threads <= 0 Then threads = 8

        Dim doUnzip = CheckUnzip.Checked AndAlso fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        Dim unzipTo = ""
        If doUnzip Then
            unzipTo = Path.Combine(folder, Path.GetFileNameWithoutExtension(fileName))
        End If

        Dim task = WarbandStartDownload(url, fileName, savePath, threads, doUnzip, unzipTo)
        AllTasks.Add(task)
        AddTaskRow(task)

        LabStartHint.Text = $"已加入下载：{fileName}（{threads} 线程）"
        TextUrl.Text = ""
        TextFileName.Text = ""
    End Sub

    ' ---------------- 任务行 ----------------

    Private Sub AddTaskRow(task As WarbandDownloadTask)
        If LabNoTask IsNot Nothing Then LabNoTask.Visibility = Visibility.Collapsed

        Dim border As New Border With {
            .CornerRadius = New CornerRadius(6),
            .Padding = New Thickness(14, 11, 14, 11),
            .Margin = New Thickness(0, 0, 0, 10)
        }
        border.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent")

        Dim panel As New StackPanel()

        Dim headGrid As New Grid()
        headGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        headGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim leftPanel As New StackPanel()
        Dim title As New TextBlock With {
            .Text = task.FileName,
            .FontWeight = FontWeights.SemiBold,
            .TextTrimming = TextTrimming.CharacterEllipsis
        }
        Dim info As New TextBlock With {.Text = "等待中", .FontSize = 12, .Margin = New Thickness(0, 4, 0, 0)}
        info.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
        leftPanel.Children.Add(title)
        leftPanel.Children.Add(info)
        Grid.SetColumn(leftPanel, 0)
        headGrid.Children.Add(leftPanel)

        Dim buttons As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Dim btnPause As New MyButton With {.Text = "暂停", .Height = 26, .Padding = New Thickness(12, 3, 12, 3), .Margin = New Thickness(0, 0, 8, 0)}
        Dim btnCancel As New MyButton With {.Text = "取消", .Height = 26, .Padding = New Thickness(12, 3, 12, 3), .Margin = New Thickness(0, 0, 8, 0)}
        Dim btnOpen As New MyButton With {.Text = "打开位置", .Height = 26, .Padding = New Thickness(12, 3, 12, 3)}
        buttons.Children.Add(btnPause)
        buttons.Children.Add(btnCancel)
        buttons.Children.Add(btnOpen)
        Grid.SetColumn(buttons, 1)
        headGrid.Children.Add(buttons)

        panel.Children.Add(headGrid)

        '进度条：底槽 + 填充
        Dim track As New Border With {
            .Height = 6,
            .CornerRadius = New CornerRadius(3),
            .Margin = New Thickness(0, 9, 0, 0),
            .HorizontalAlignment = HorizontalAlignment.Stretch
        }
        track.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent")

        Dim fill As New Border With {
            .Height = 6,
            .CornerRadius = New CornerRadius(3),
            .HorizontalAlignment = HorizontalAlignment.Left,
            .Width = 0
        }
        fill.SetResourceReference(Border.BackgroundProperty, "ColorBrush2")
        track.Child = fill
        panel.Children.Add(track)

        border.Child = panel
        PanTasks.Children.Add(border)

        Dim row As New TaskRow With {
            .Task = task,
            .Track = track,
            .Fill = fill,
            .LabTitle = title,
            .LabInfo = info,
            .BtnPause = btnPause,
            .BtnCancel = btnCancel,
            .BtnOpen = btnOpen
        }
        Rows.Add(row)

        AddHandler btnPause.Click, Sub(s, e2)
                                       If row.Task.State = WarbandDownloadState.Running OrElse row.Task.State = WarbandDownloadState.Waiting Then
                                           WarbandPauseDownload(row.Task)
                                           row.Task.State = WarbandDownloadState.Paused
                                       ElseIf row.Task.State = WarbandDownloadState.Paused OrElse row.Task.State = WarbandDownloadState.Failed Then
                                           WarbandResumeDownload(row.Task)
                                       End If
                                       RefreshTaskRows(Nothing, Nothing)
                                   End Sub

        AddHandler btnCancel.Click, Sub(s, e2)
                                        WarbandCancelDownload(row.Task)
                                        RefreshTaskRows(Nothing, Nothing)
                                    End Sub

        AddHandler btnOpen.Click, Sub(s, e2) OpenTaskLocation(row.Task)

        AddHandler border.SizeChanged, Sub(s, e2) UpdateFillWidth(row)
        AddHandler track.SizeChanged, Sub(s, e2) UpdateFillWidth(row)
        RefreshTaskRows(Nothing, Nothing)
    End Sub

    Private Sub UpdateFillWidth(row As TaskRow)
        Try
            Dim total = row.Track.ActualWidth
            If total <= 0 Then Return
            row.Fill.Width = Math.Max(0, Math.Min(total, total * row.Task.Progress01))
        Catch
        End Try
    End Sub

    Private Sub OpenTaskLocation(task As WarbandDownloadTask)
        Try
            If State_Completed(task) AndAlso File.Exists(task.SavePath) Then
                Process.Start("explorer.exe", "/select,""" & task.SavePath & """")
            Else
                Dim dir = Path.GetDirectoryName(task.SavePath)
                If Directory.Exists(dir) Then Process.Start("explorer.exe", dir)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 打开下载位置失败")
        End Try
    End Sub

    Private Shared Function State_Completed(task As WarbandDownloadTask) As Boolean
        Return task.State = WarbandDownloadState.Completed
    End Function

    ' ---------------- 刷新 ----------------

    Private Sub RefreshTaskRows(sender As Object, e As EventArgs)
        If Rows.Count = 0 Then Return

        For Each row In Rows
            Dim task = row.Task

            Dim parts As New List(Of String)
            parts.Add(task.StateText)
            If task.ProgressText <> "" Then parts.Add(task.ProgressText)
            If task.SpeedText <> "" Then parts.Add(task.SpeedText)
            If task.EtaText <> "" Then parts.Add("剩余 " & task.EtaText)
            If task.State = WarbandDownloadState.Completed AndAlso task.MessageText <> "" Then parts.Add(task.MessageText)
            row.LabInfo.Text = String.Join("  ·  ", parts)
            If task.State = WarbandDownloadState.Failed Then
                row.LabInfo.Foreground = New SolidColorBrush(Color.FromRgb(&HD5, &H41, &H3E))
            Else
                row.LabInfo.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
            End If

            row.BtnPause.Text = If(task.State = WarbandDownloadState.Paused OrElse task.State = WarbandDownloadState.Failed, "继续", "暂停")
            row.BtnPause.IsEnabled = task.State <> WarbandDownloadState.Completed AndAlso task.State <> WarbandDownloadState.Canceled

            UpdateFillWidth(row)
        Next
    End Sub

    ' ---------------- 网盘入口 ----------------

    Private Sub BtnOpenBaidu_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenBaidu.Click
        WarbandOpenUrl("https://pan.baidu.com/")
    End Sub

    Private Sub BtnOpen123_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpen123.Click
        WarbandOpenUrl("https://www.123pan.com/")
    End Sub

    Private Sub BtnOpenQuark_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenQuark.Click
        WarbandOpenUrl("https://pan.quark.cn/")
    End Sub

    Private Sub BtnOpenChineseSite_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenChineseSite.Click
        WarbandOpenUrl(WarbandChineseSite)
    End Sub

End Class
