Imports System.IO

''' <summary>战团启动页：沿用 PCL 的页面与控件，只替换「启动什么」。</summary>
Public Class PageWarbandRight

    Private Structure KnobDef
        Public Section As String
        Public Key As String
        Public Label As String
        Public Hint As String
    End Structure

    Private Class KnobRow
        Public Def As KnobDef
        Public Box As MyTextBox
    End Class

    Private ReadOnly KnobDefs As KnobDef() = {
        New KnobDef With {.Section = "Graphics", .Key = "bOnDemandTextures", .Label = "按需加载纹理", .Hint = "true / false，省显存"},
        New KnobDef With {.Section = "Graphics", .Key = "iTextureQuality", .Label = "纹理质量", .Hint = "0–3"},
        New KnobDef With {.Section = "Graphics", .Key = "iAnisotropicFiltering", .Label = "各向异性过滤", .Hint = "0–4"},
        New KnobDef With {.Section = "Graphics", .Key = "iMultisampleType", .Label = "多重采样 MSAA", .Hint = "0 / 2 / 4 / 8"},
        New KnobDef With {.Section = "General", .Key = "iMaxFrameRate", .Label = "帧率上限", .Hint = "整数，0 为不限制"},
        New KnobDef With {.Section = "General", .Key = "iNumHints", .Label = "界面提示数", .Hint = "0 可减少开销"},
        New KnobDef With {.Section = "ShadowMap", .Key = "bShadowMap", .Label = "动态阴影", .Hint = "true / false"},
        New KnobDef With {.Section = "ShadowMap", .Key = "iShadowMapTextureSize", .Label = "阴影贴图尺寸", .Hint = "512 / 1024 / 2048 / 4096"},
        New KnobDef With {.Section = "ShadowMap", .Key = "iShadowMapNumPasses", .Label = "阴影分级层数", .Hint = "1–4"},
        New KnobDef With {.Section = "Battle", .Key = "iMaxNumCorpses", .Label = "尸体上限", .Hint = "整数"},
        New KnobDef With {.Section = "Battle", .Key = "iMaxNumRagdolls", .Label = "布娃娃上限", .Hint = "整数"},
        New KnobDef With {.Section = "Threads", .Key = "iNumTaskThreads", .Label = "任务线程数", .Hint = "建议 = 物理核心数"},
        New KnobDef With {.Section = "PostFx", .Key = "iFxaa", .Label = "FXAA 抗锯齿", .Hint = "0 / 1"},
        New KnobDef With {.Section = "Sound", .Key = "bSound", .Label = "音效", .Hint = "true / false"},
        New KnobDef With {.Section = "Sound", .Key = "bMusic", .Label = "音乐", .Hint = "true / false"},
        New KnobDef With {.Section = "Sound", .Key = "bHrtfFilter", .Label = "HRTF 滤波", .Hint = "true / false，关掉更省 CPU"}
    }

    Private ReadOnly KnobRows As New List(Of KnobRow)
    Private ReadOnly WarbandModules As New List(Of WarbandModuleInfo)
    Private IsWarbandInitialized As Boolean
    Private IsApplyingState As Boolean

    Private Sub PageWarbandRight_Loaded() Handles Me.Loaded
        If IsWarbandInitialized Then
            RefreshChecks()
            Return
        End If
        IsWarbandInitialized = True

        IsApplyingState = True
        WarbandLoadSettings()

        If String.IsNullOrWhiteSpace(WarbandGamePath) OrElse Not Directory.Exists(WarbandGamePath) Then
            Dim detected = WarbandDetectGamePath()
            If detected <> "" Then WarbandGamePath = detected
        End If

        TextGamePath.Text = If(WarbandGamePath, "")
        CheckNoIntro.Checked = WarbandNoIntro
        TextExtraArgs.Text = If(WarbandExtraArgs, "")
        SelectRuntimeRadio(WarbandSelectedRuntime)
        IsApplyingState = False

        LoadWarbandModules()
        LoadKnobs()
        RefreshWse2Status()
        RefreshChecks()
        RefreshLogTail()
        UpdateCommandPreview()
    End Sub

    ' ---------------- 游戏目录 ----------------

    Private Sub BtnDetect_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnDetect.Click
        Dim detected = WarbandDetectGamePath()
        If detected = "" Then
            LabGameStatus.Text = "未能自动定位 Warband，请手动选择游戏目录。"
            Return
        End If
        TextGamePath.Text = detected
        ApplyGamePath()
    End Sub

    Private Sub BtnBrowse_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnBrowse.Click
        Dim dialog As New Ookii.Dialogs.Wpf.VistaFolderBrowserDialog With {
            .Description = "选择 Mount & Blade Warband 安装目录",
            .UseDescriptionForTitle = True
        }
        If Directory.Exists(TextGamePath.Text) Then dialog.SelectedPath = TextGamePath.Text
        If dialog.ShowDialog() = True Then
            TextGamePath.Text = dialog.SelectedPath
            ApplyGamePath()
        End If
    End Sub

    Private Sub BtnApplyPath_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnApplyPath.Click
        ApplyGamePath()
    End Sub

    Private Sub ApplyGamePath()
        Dim path = TextGamePath.Text.Trim()
        If Not Directory.Exists(path) Then
            LabGameStatus.Text = "目录不存在：" & path
            Return
        End If
        WarbandGamePath = path
        WarbandSaveSettings()
        LoadWarbandModules()
        RefreshWse2Status()
        RefreshChecks()
        RefreshLogTail()
        UpdateCommandPreview()
    End Sub

    ' ---------------- 模块 ----------------

    ''' <summary>取当前下拉框选中的模块（下拉项是 MyComboBoxItem，Tag 里放模块对象）。</summary>
    Private Function SelectedWarbandModule() As WarbandModuleInfo
        Dim item = TryCast(ComboModule.SelectedItem, MyComboBoxItem)
        If item Is Nothing Then Return Nothing
        Return TryCast(item.Tag, WarbandModuleInfo)
    End Function
    Private Sub LoadWarbandModules()
        WarbandModules.Clear()
        Dim scanned = WarbandScanModules(WarbandGamePath)
        WarbandModules.AddRange(scanned)

        ComboModule.Items.Clear()
        For Each item In WarbandModules
            ComboModule.Items.Add(New MyComboBoxItem With {.Content = item.DisplayName, .Tag = item})
        Next

        If WarbandModules.Count = 0 Then
            LabGameStatus.Text = "没有找到模块：请确认 " & If(WarbandGamePath, "（未设置目录）") & "\Modules 存在。"
            ComboModule.SelectedIndex = -1
            Return
        End If

        '优先用上次保存的模块；其次 Native；最后才取第一个。绝不再按体积/注册表猜。
        Dim index = WarbandModules.FindIndex(Function(m) m.Folder.Equals(WarbandModule, StringComparison.OrdinalIgnoreCase))
        If index < 0 Then index = WarbandModules.FindIndex(Function(m) m.Folder.Equals("Native", StringComparison.OrdinalIgnoreCase))
        If index < 0 Then index = 0

        IsApplyingState = True
        ComboModule.SelectedIndex = index
        IsApplyingState = False
        '只有用户主动改下拉框时才写回设置；这里绝不覆盖已保存的模块名，避免“总是启动同一个 mod”
        If Not WarbandModules(index).Folder.Equals(WarbandModule, StringComparison.OrdinalIgnoreCase) Then
            LabStatus.Text = $"上次选的模块「{If(WarbandModule, "空")}」没找到，本次临时用 {WarbandModules(index).Folder}（选择后会记住）"
        End If

        Dim x86 = WarbandExePath(WarbandGamePath, WarbandRuntime.X86) <> ""
        Dim x64 = WarbandExePath(WarbandGamePath, WarbandRuntime.X64) <> ""
        Dim ded = WarbandExePath(WarbandGamePath, WarbandRuntime.Dedicated) <> ""
        LabGameStatus.Text = $"已识别：x86 {If(x86, "✓", "缺失")} · x64 {If(x64, "✓", "缺失")} · 专用服务器 {If(ded, "✓", "缺失")}　共 {WarbandModules.Count} 个模块"
        WarbandSaveSettings()
    End Sub

    Private Sub BtnRefreshModules_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnRefreshModules.Click
        LoadWarbandModules()
        RefreshChecks()
        UpdateCommandPreview()
        RefreshWse2Status()
    End Sub

    Private Sub ComboModule_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboModule.SelectionChanged
        If IsApplyingState Then Return
        Dim selected = SelectedWarbandModule()
        If selected Is Nothing Then Return
        WarbandModule = selected.Folder
        WarbandSaveSettings()
        RefreshChecks()
        UpdateCommandPreview()
        LabStatus.Text = "将启动：" & selected.Folder
        If FrmWarbandLeft IsNot Nothing Then FrmWarbandLeft.RefreshWarbandLeft()
    End Sub

    ' ---------------- 启动选项 ----------------

    Private Sub RadioRuntime_Check(sender As Object, raiseByMouse As Boolean) _
        Handles RadioX86.Check, RadioX64.Check
        If IsApplyingState Then Return
        Dim button = CType(sender, MyRadioButton)
        WarbandSelectedRuntime = CType(Val(button.Tag), WarbandRuntime)
        WarbandSaveSettings()
        RefreshChecks()
        UpdateCommandPreview()
    End Sub

    Private Sub SelectRuntimeRadio(runtime As WarbandRuntime)
        Dim wasApplying = IsApplyingState
        IsApplyingState = True
        '界面只暴露两档（与 WSE2 官方启动器一致）；专用服务器运行时会回落到 x64
        If runtime = WarbandRuntime.X86 Then RadioX86.Checked = True Else RadioX64.Checked = True
        IsApplyingState = wasApplying
    End Sub

    Private Sub CheckNoIntro_Change(sender As Object, user As Boolean) Handles CheckNoIntro.Change
        If IsApplyingState Then Return
        WarbandNoIntro = CheckNoIntro.Checked
        WarbandSaveSettings()
        UpdateCommandPreview()
    End Sub

    Private Sub TextExtraArgs_TextChanged(sender As Object, e As TextChangedEventArgs) Handles TextExtraArgs.TextChanged
        If IsApplyingState Then Return
        WarbandExtraArgs = TextExtraArgs.Text
        WarbandSaveSettings()
        UpdateCommandPreview()
    End Sub

    Private Sub UpdateCommandPreview()
        Dim selected = SelectedWarbandModule()
        If selected Is Nothing OrElse String.IsNullOrWhiteSpace(WarbandGamePath) Then
            LabCommand.Text = "（选择游戏目录与模块后显示将要执行的命令行）"
            Return
        End If
        LabCommand.Text = WarbandExePath(WarbandGamePath, WarbandSelectedRuntime) &
                          " " & WarbandBuildArguments(selected, WarbandSelectedRuntime, WarbandNoIntro, WarbandExtraArgs)
    End Sub

    ' ---------------- 启动 / 结束 ----------------

    Private Sub BtnLaunch_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLaunch.Click
        '以设置里持久化的模块名为准；下拉框只是它的显示层
        Dim selected = WarbandFindModule(WarbandGamePath, WarbandModule)
        If selected Is Nothing Then selected = SelectedWarbandModule()
        If selected IsNot Nothing Then WarbandModule = selected.Folder
        Dim errorMessage = WarbandLaunch(WarbandGamePath, selected, WarbandSelectedRuntime, WarbandNoIntro, WarbandExtraArgs)
        LabStatus.Text = If(errorMessage = "", "已启动，等待游戏窗口出现…", errorMessage)
        If errorMessage = "" Then
            RefreshLogTail()
        End If
    End Sub

    Private Sub BtnStop_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnStop.Click
        Dim names = {Path.GetFileNameWithoutExtension(WarbandExeX86),
                     Path.GetFileNameWithoutExtension(WarbandExeX64),
                     Path.GetFileNameWithoutExtension(WarbandExeDedicated),
                     Path.GetFileNameWithoutExtension(WarbandExeDedicatedX64)}
        Dim killed = 0
        For Each exeStem In names
            For Each warbandProcess In Process.GetProcessesByName(exeStem)
                Try
                    warbandProcess.Kill()
                    killed += 1
                Catch ex As Exception
                    Logger.Warn(ex, "结束战团进程失败")
                End Try
            Next
        Next
        WarbandRunning = False
        LabStatus.Text = If(killed > 0, $"已结束 {killed} 个进程。", "没有发现正在运行的战团进程。")
    End Sub

    ' ---------------- 体检 / 诊断 / 日志 ----------------

    Private Sub RefreshChecks()
        PanChecks.Children.Clear()
        Dim selected = SelectedWarbandModule()
        For Each line In WarbandPreflight(WarbandGamePath, selected, WarbandSelectedRuntime)
            Dim text As New TextBlock With {
                .Text = line,
                .TextWrapping = TextWrapping.Wrap,
                .Margin = New Thickness(0, 0, 0, 6)
            }
            Select Case line.Substring(0, 1)
                Case "✓" : text.Foreground = New SolidColorBrush(Color.FromRgb(&H2E, &HA0, &H61))
                Case "!" : text.Foreground = New SolidColorBrush(Color.FromRgb(&HDC, &H9A, &H29))
                Case Else : text.Foreground = New SolidColorBrush(Color.FromRgb(&HD5, &H41, &H3E))
            End Select
            PanChecks.Children.Add(text)
        Next
    End Sub

    Private Sub BtnRecheck_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnRecheck.Click
        RefreshChecks()
    End Sub

    Private Sub BtnDiagnose_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnDiagnose.Click
        LabDiagnosis.Text = WarbandDiagnose(WarbandGamePath)
    End Sub

    Private Sub BtnRefreshLog_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnRefreshLog.Click
        RefreshLogTail()
    End Sub

    Private Sub RefreshLogTail()
        LabLog.Text = If(String.IsNullOrWhiteSpace(WarbandGamePath),
                         "尚未设置游戏目录。",
                         WarbandReadLogTail(WarbandGamePath, 120))
    End Sub

    ' ---------------- WSE2 配置 ----------------

    Private Sub LoadKnobs()
        PanKnobs.Children.Clear()
        KnobRows.Clear()
        Dim lines = WarbandReadConfigLines()

        For Each def In KnobDefs
            Dim row As New Grid With {.Margin = New Thickness(0, 0, 0, 8)}
            row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(190)})
            row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(130)})
            row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})

            Dim label As New TextBlock With {.Text = def.Label, .VerticalAlignment = VerticalAlignment.Center}
            Grid.SetColumn(label, 0)
            row.Children.Add(label)

            Dim box As New MyTextBox With {
                .Text = WarbandGetConfig(lines, def.Section, def.Key, "未设置"),
                .Height = 26,
                .Margin = New Thickness(0, 0, 12, 0),
                .VerticalAlignment = VerticalAlignment.Center
            }
            Grid.SetColumn(box, 1)
            row.Children.Add(box)

            Dim hint As New TextBlock With {
                .Text = $"[{def.Section}] {def.Hint}",
                .FontSize = 12,
                .VerticalAlignment = VerticalAlignment.Center,
                .Foreground = New SolidColorBrush(Color.FromRgb(&H88, &H88, &H88))
            }
            Grid.SetColumn(hint, 2)
            row.Children.Add(hint)

            PanKnobs.Children.Add(row)
            KnobRows.Add(New KnobRow With {.Def = def, .Box = box})
        Next

        LabConfigStatus.Text = If(File.Exists(WarbandConfigPath),
                                  "已加载 " & WarbandConfigPath,
                                  "未找到 rgl_config.ini，保存时会新建（先运行一次游戏可生成完整默认值）。")
    End Sub

    Private Sub BtnSaveConfig_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSaveConfig.Click
        SaveKnobs(Nothing)
    End Sub

    Private Sub SaveKnobs(preset As Dictionary(Of String, String))
        Dim lines = WarbandReadConfigLines()
        Dim changed = 0

        If preset IsNot Nothing Then
            For Each pair In preset
                Dim slash = pair.Key.IndexOf("/"c)
                If slash <= 0 Then Continue For
                WarbandSetConfig(lines, pair.Key.Substring(0, slash), pair.Key.Substring(slash + 1), pair.Value)
                changed += 1
            Next
        End If

        For Each row In KnobRows
            Dim value = row.Box.Text.Trim()
            If value = "" OrElse value = "未设置" Then Continue For
            If WarbandSetConfig(lines, row.Def.Section, row.Def.Key, value) Then changed += 1
        Next

        Try
            Dim backup = WarbandSaveConfig(lines)
            LabConfigStatus.Text = If(backup = "",
                                      $"已写入 {changed} 项（新建配置）。",
                                      $"已写入 {changed} 项，原文件备份为 {backup}")
            LoadKnobs()
        Catch ex As Exception
            LabConfigStatus.Text = "保存失败：" & ex.Message
            Logger.Error(ex, "[Warband] 保存 rgl_config.ini 失败")
        End Try
    End Sub

    Private Sub BtnPresetLow_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPresetLow.Click
        SaveKnobs(BuildPreset("low"))
    End Sub

    Private Sub BtnPresetMid_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPresetMid.Click
        SaveKnobs(BuildPreset("mid"))
    End Sub

    Private Sub BtnPresetHigh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPresetHigh.Click
        SaveKnobs(BuildPreset("high"))
    End Sub

    Private Shared Function BuildPreset(level As String) As Dictionary(Of String, String)
        Dim values As New Dictionary(Of String, String)
        Select Case level
            Case "low"
                values("Graphics/bOnDemandTextures") = "true"
                values("Graphics/iTextureQuality") = "1"
                values("Graphics/iAnisotropicFiltering") = "0"
                values("Graphics/iMultisampleType") = "0"
                values("General/iMaxFrameRate") = "60"
                values("ShadowMap/iShadowMapTextureSize") = "512"
                values("ShadowMap/iShadowMapNumPasses") = "2"
                values("Battle/iMaxNumCorpses") = "500"
                values("Battle/iMaxNumRagdolls") = "40"
                values("Threads/iNumTaskThreads") = "2"
                values("PostFx/iFxaa") = "0"
            Case "high"
                values("Graphics/bOnDemandTextures") = "true"
                values("Graphics/iTextureQuality") = "3"
                values("Graphics/iAnisotropicFiltering") = "4"
                values("Graphics/iMultisampleType") = "4"
                values("General/iMaxFrameRate") = "144"
                values("ShadowMap/iShadowMapTextureSize") = "2048"
                values("ShadowMap/iShadowMapNumPasses") = "4"
                values("Battle/iMaxNumCorpses") = "2000"
                values("Battle/iMaxNumRagdolls") = "100"
                values("Threads/iNumTaskThreads") = "4"
                values("PostFx/iFxaa") = "1"
            Case Else
                values("Graphics/bOnDemandTextures") = "true"
                values("Graphics/iTextureQuality") = "2"
                values("Graphics/iAnisotropicFiltering") = "2"
                values("Graphics/iMultisampleType") = "0"
                values("General/iMaxFrameRate") = "120"
                values("ShadowMap/iShadowMapTextureSize") = "1024"
                values("ShadowMap/iShadowMapNumPasses") = "3"
                values("Battle/iMaxNumCorpses") = "2000"
                values("Battle/iMaxNumRagdolls") = "100"
                values("Threads/iNumTaskThreads") = "3"
                values("PostFx/iFxaa") = "1"
        End Select
        Return values
    End Function

    Private Sub BtnOpenConfigDir_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenConfigDir.Click
        Try
            Directory.CreateDirectory(WarbandConfigDir)
            Process.Start("explorer.exe", WarbandConfigDir)
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 打开配置目录失败")
        End Try
    End Sub
    ' ---------------- 内置 WSE2 ----------------

    Private Sub RefreshWse2Status()
        If LabWse2Status Is Nothing Then Return
        LabWse2Status.Text = WarbandWse2StatusText(WarbandGamePath)
        BtnInstallWse2.IsEnabled = WarbandHasBundledWse2()
        If Not WarbandHasBundledWse2() Then
            LabWse2Status.Text &= "（本包未内置 WSE2，请用「从 zip 安装…」）"
        End If
    End Sub

    Private Sub BtnInstallWse2_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnInstallWse2.Click
        If String.IsNullOrWhiteSpace(WarbandGamePath) OrElse Not Directory.Exists(WarbandGamePath) Then
            LabStatus.Text = "先设置游戏目录，才能安装 WSE2。"
            Return
        End If

        BtnInstallWse2.IsEnabled = False
        LabStatus.Text = "正在安装 WSE2…"
        UpdateLayout()
        Try
            Dim report = WarbandInstallBundledWse2(WarbandGamePath)
            LabStatus.Text = report.Replace(vbCrLf, " ").Trim()
            Hint("WSE2 安装完成", HintType.Blue)
        Finally
            BtnInstallWse2.IsEnabled = True
            RefreshWse2Status()
            RefreshChecks()
            UpdateCommandPreview()
        End Try
    End Sub

    Private Sub BtnInstallWse2Zip_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnInstallWse2Zip.Click
        Dim dialog As New Microsoft.Win32.OpenFileDialog With {
            .Title = "选择 WSE2 安装包（zip）",
            .Filter = "WSE2 压缩包|*.zip|所有文件|*.*"
        }
        If dialog.ShowDialog() <> True Then Return
        If String.IsNullOrWhiteSpace(WarbandGamePath) OrElse Not Directory.Exists(WarbandGamePath) Then
            LabStatus.Text = "先设置游戏目录，才能安装 WSE2。"
            Return
        End If
        LabStatus.Text = WarbandInstallWse2FromZip(dialog.FileName, WarbandGamePath).Replace(vbCrLf, " ").Trim()
        RefreshWse2Status()
        RefreshChecks()
        UpdateCommandPreview()
    End Sub

    Private Sub BtnWse2Official_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnWse2Official.Click
        'WSE2 的官方发布页（Taleworlds 论坛原帖），用来查版本与更新日志
        WarbandOpenUrl("https://forums.taleworlds.com/index.php?threads/warband-script-enhancer-2.384882/")
    End Sub
End Class