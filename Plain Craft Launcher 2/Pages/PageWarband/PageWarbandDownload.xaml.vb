Public Class PageWarbandDownload

    Private WarbandEntries As New List(Of WarbandModEntry)
    Private EditingEntry As WarbandModEntry
    Private IsWarbandDownloadLoaded As Boolean

    Private Sub PageWarbandDownload_Loaded() Handles Me.Loaded
        If IsWarbandDownloadLoaded Then Return
        IsWarbandDownloadLoaded = True
        ReloadLibrary()
    End Sub

    Private Sub ReloadLibrary()
        WarbandEntries = WarbandLoadModLibrary()
        RenderEntries()
    End Sub

    Private Sub RenderEntries()
        If PanEntries Is Nothing Then Return
        PanEntries.Children.Clear()

        Dim filterText = If(TextSearch.Text, "").Trim()
        Dim shown = 0

        For Each entry In WarbandEntries
            If filterText <> "" AndAlso entry.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
            PanEntries.Children.Add(BuildEntryRow(entry))
            shown += 1
        Next

        If shown = 0 Then
            Dim emptyText As New TextBlock With {
                .Text = "没有匹配的条目。可以点「打开数据文件」手动添加，或直接在 mods.json 里补一条。",
                .TextWrapping = TextWrapping.Wrap,
                .Margin = New Thickness(0, 6, 0, 0)
            }
            emptyText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
            PanEntries.Children.Add(emptyText)
        End If
    End Sub

    Private Function BuildEntryRow(entry As WarbandModEntry) As FrameworkElement
        Dim border As New Border With {
            .CornerRadius = New CornerRadius(5),
            .Padding = New Thickness(13, 10, 13, 10),
            .Margin = New Thickness(0, 0, 0, 8)
        }
        border.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent")

        Dim grid As New Grid()
        grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        grid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim textPanel As New StackPanel()
        Dim titleText As New TextBlock With {.Text = entry.Name, .FontWeight = FontWeights.SemiBold, .TextWrapping = TextWrapping.Wrap}
        Dim noteText As New TextBlock With {.Text = entry.Note, .FontSize = 12, .Margin = New Thickness(0, 3, 0, 0), .TextWrapping = TextWrapping.Wrap}
        noteText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
        textPanel.Children.Add(titleText)

        Dim extra As New List(Of String)
        If Not String.IsNullOrWhiteSpace(entry.PanUrl) Then extra.Add("已填写网盘")
        If extra.Count > 0 Then noteText.Text = entry.Note & " · " & String.Join(" · ", extra)
        textPanel.Children.Add(noteText)

        Grid.SetColumn(textPanel, 0)
        grid.Children.Add(textPanel)

        Dim buttons As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}

        Dim siteButton As New MyButton With {.Text = "中文站搜索", .Height = 26, .Padding = New Thickness(12, 4, 12, 4), .Margin = New Thickness(0, 0, 8, 0)}
        AddHandler siteButton.Click, Sub(sender, e)
                                         WarbandOpenUrl(If(String.IsNullOrWhiteSpace(entry.ChineseSearchUrl), WarbandChineseSearchUrl(entry.Name), entry.ChineseSearchUrl))
                                     End Sub
        buttons.Children.Add(siteButton)

        If Not String.IsNullOrWhiteSpace(entry.PanUrl) Then
            Dim panButton As New MyButton With {.Text = "打开网盘", .Height = 26, .Padding = New Thickness(12, 4, 12, 4), .Margin = New Thickness(0, 0, 8, 0)}
            AddHandler panButton.Click, Sub(sender, e)
                                            WarbandOpenUrl(entry.PanUrl)
                                            If Not String.IsNullOrWhiteSpace(entry.PanCode) Then
                                                Try
                                                    Clipboard.SetText(entry.PanCode)
                                                Catch ex As Exception
                                                    Logger.Warn(ex, "[Warband] 复制提取码失败")
                                                End Try
                                            End If
                                        End Sub
            buttons.Children.Add(panButton)
        End If

        Dim editButton As New MyButton With {.Text = "填写网盘", .Height = 26, .Padding = New Thickness(12, 4, 12, 4)}
        AddHandler editButton.Click, Sub(sender, e) BeginEdit(entry)
        buttons.Children.Add(editButton)

        Grid.SetColumn(buttons, 1)
        grid.Children.Add(buttons)

        border.Child = grid
        Return border
    End Function

    Private Sub BeginEdit(entry As WarbandModEntry)
        EditingEntry = entry
        LabEditing.Text = "正在编辑：" & entry.Name
        TextPanUrl.Text = If(entry.PanUrl, "")
        TextPanCode.Text = If(entry.PanCode, "")
        LabSaveStatus.Text = ""
    End Sub

    Private Sub BtnSaveEntry_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSaveEntry.Click
        If EditingEntry Is Nothing Then
            LabSaveStatus.Text = "先在列表里点某个条目的「填写网盘」。"
            Return
        End If

        EditingEntry.PanUrl = If(TextPanUrl.Text, "").Trim()
        EditingEntry.PanCode = If(TextPanCode.Text, "").Trim()
        WarbandSaveModLibrary(WarbandEntries)
        LabSaveStatus.Text = "已保存到本机 mods.json"
        RenderEntries()
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnRefresh.Click
        ReloadLibrary()
        LabSaveStatus.Text = "已重新读取。"
    End Sub

    Private Sub BtnOpenData_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenData.Click
        Try
            Directory.CreateDirectory(WarbandDataDir)
            If Not File.Exists(WarbandModLibraryFile) Then WarbandSaveModLibrary(WarbandEntries)
            Process.Start("notepad.exe", WarbandModLibraryFile)
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 打开 mods.json 失败")
        End Try
    End Sub

    Private Sub BtnChineseSite_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnChineseSite.Click
        WarbandOpenUrl(WarbandChineseSite)
    End Sub

    Private Sub TextSearch_TextChanged(sender As Object, e As TextChangedEventArgs) Handles TextSearch.TextChanged
        RenderEntries()
    End Sub

End Class
