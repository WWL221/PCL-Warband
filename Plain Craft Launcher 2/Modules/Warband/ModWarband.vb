' PCL 战团版（PCL Warband）——骑砍战团 / WSE2 启动支持
' 本模块只负责「战团侧」的逻辑，界面在 Pages\PageWarband 下，沿用 PCL 的页面与控件框架。
'
' 命令行与 WSE2 官方启动器保持一致：
'   客户端   ：mb_warband_wse2[_x64].exe --module "<模块文件夹名>" --no-intro
'   专用服务器：mb_warband_wse2_dedicated[_x64].exe --config-path server_config.ini -r "<模块文件夹名>.txt" --module "<模块文件夹名>"
' 不注入 DLL、不改动任何游戏文件；rgl_config.ini 写入前自动备份并以临时文件原子替换。

Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports Microsoft.Win32

''' <summary>Modules 目录下的一个模块。</summary>
Public Class WarbandModuleInfo
    Public Property Folder As String = ""
    Public Property FullPath As String = ""
    ''' <summary>module.ini 中的 module_name（绝大多数 mod 都写成 Calradia，不能当作展示名）。</summary>
    Public Property InternalName As String = ""
    Public Property Compatible As String = ""
    Public Property HasModuleIni As Boolean
    Public Property ResourceSize As Long
    Public Property BigFiles As New List(Of String)

    Public ReadOnly Property ResourceText As String
        Get
            Return WarbandUtil.FormatSize(ResourceSize)
        End Get
    End Property

    Public ReadOnly Property DisplayName As String
        Get
            Return Folder
        End Get
    End Property

    Public ReadOnly Property DetailText As String
        Get
            Dim parts As New List(Of String)
            If InternalName <> "" Then parts.Add(InternalName)
            If Compatible <> "" Then parts.Add("兼容 " & Compatible)
            parts.Add(ResourceText)
            If Not HasModuleIni Then parts.Add("无 module.ini")
            Return String.Join(" · ", parts)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return DisplayName
    End Function
End Class

Public Enum WarbandRuntime
    X86 = 0
    X64 = 1
    Dedicated = 2
    DedicatedX64 = 3
End Enum

''' <summary>通用小工具。</summary>
Public Module WarbandUtil
    Public Function FormatSize(bytes As Long) As String
        If bytes <= 0 Then Return "0 B"
        Dim units = {"B", "KB", "MB", "GB", "TB"}
        Dim value As Double = bytes
        Dim unit = 0
        While value >= 1024 AndAlso unit < units.Length - 1
            value /= 1024
            unit += 1
        End While
        Return If(unit <= 1, $"{value:0} {units(unit)}", $"{value:0.0} {units(unit)}")
    End Function

    Public Function StripInlineComment(raw As String) As String
        For i = 0 To raw.Length - 1
            If (raw(i) = "#"c OrElse raw(i) = ";"c) AndAlso (i = 0 OrElse Char.IsWhiteSpace(raw(i - 1))) Then
                Return raw.Substring(0, i).Trim()
            End If
        Next
        Return raw.Trim()
    End Function
End Module

''' <summary>战团 / WSE2 的定位、扫描、启动、配置与诊断。</summary>
Public Module ModWarband

    Public Const WarbandExeX86 As String = "mb_warband_wse2.exe"
    Public Const WarbandExeX64 As String = "mb_warband_wse2_x64.exe"
    Public Const WarbandExeDedicated As String = "mb_warband_wse2_dedicated.exe"
    Public Const WarbandExeDedicatedX64 As String = "mb_warband_wse2_dedicated_x64.exe"
    Public Const WarbandSteamAppId As String = "48700"

    ''' <summary>用 PCL 自带的「隐藏页面」设置把顶部栏收敛成「下载 + 战团」。</summary>
    Public Sub WarbandApplyTrimmedNav()
        Try
            Settings.Set("UiHiddenPageLink", True)
            Settings.Set("UiHiddenPageSetup", True)
            Settings.Set("UiHiddenPageOther", True)
            Settings.Set("UiHiddenPageDownload", False)
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 裁剪顶部导航失败")
        End Try
    End Sub
    ' ---------------- 版本相关的显示文字 ----------------

    Public Function RuntimeName(runtime As WarbandRuntime) As String
        Select Case runtime
            Case WarbandRuntime.X86 : Return "x86 客户端（32 位，2GB 地址空间）"
            Case WarbandRuntime.X64 : Return "x64 客户端（64 位，推荐）"
            Case WarbandRuntime.Dedicated : Return "专用服务器 x86"
            Case WarbandRuntime.DedicatedX64 : Return "专用服务器 x64"
            Case Else : Return runtime.ToString()
        End Select
    End Function

    Public Function RuntimeExe(runtime As WarbandRuntime) As String
        Select Case runtime
            Case WarbandRuntime.X86 : Return WarbandExeX86
            Case WarbandRuntime.X64 : Return WarbandExeX64
            Case WarbandRuntime.Dedicated : Return WarbandExeDedicated
            Case WarbandRuntime.DedicatedX64 : Return WarbandExeDedicatedX64
            Case Else : Return WarbandExeX64
        End Select
    End Function

    Public Function IsDedicated(runtime As WarbandRuntime) As Boolean
        Return runtime = WarbandRuntime.Dedicated OrElse runtime = WarbandRuntime.DedicatedX64
    End Function

    Public Function Is32Bit(runtime As WarbandRuntime) As Boolean
        Return runtime = WarbandRuntime.X86 OrElse runtime = WarbandRuntime.Dedicated
    End Function

    ' ---------------- 设置持久化（%APPDATA%\PCL Warband\warband.txt） ----------------

    Public ReadOnly Property WarbandDataDir As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL Warband")

    Private ReadOnly Property WarbandSettingsFile As String = Path.Combine(WarbandDataDir, "warband.txt")

    Public Property WarbandGamePath As String
    Public Property WarbandModule As String = ""
    Public Property WarbandSelectedRuntime As WarbandRuntime = WarbandRuntime.X64
    Public Property WarbandNoIntro As Boolean = True
    Public Property WarbandExtraArgs As String = ""
    Public Property WarbandRunning As Boolean = False

    Public Sub WarbandLoadSettings()
        Try
            If Not File.Exists(WarbandSettingsFile) Then Return
            For Each line In File.ReadAllLines(WarbandSettingsFile)
                Dim index = line.IndexOf("="c)
                If index <= 0 Then Continue For
                Dim key = line.Substring(0, index).Trim()
                Dim value = line.Substring(index + 1).Trim()
                Select Case key
                    Case "GamePath" : WarbandGamePath = value
                    Case "Module" : WarbandModule = value
                    Case "Runtime" : WarbandSelectedRuntime = If(Val(value) = 0, WarbandRuntime.X86, WarbandRuntime.X64)
                    Case "NoIntro" : WarbandNoIntro = (value = "1")
                    Case "ExtraArgs" : WarbandExtraArgs = value
                End Select
            Next
        Catch ex As Exception
            Logger.Warn(ex, "读取战团设置失败")
        End Try
    End Sub

    Public Sub WarbandSaveSettings()
        Try
            Directory.CreateDirectory(WarbandDataDir)
            Dim sb As New StringBuilder()
            sb.AppendLine("GamePath=" & If(WarbandGamePath, ""))
            sb.AppendLine("Module=" & If(WarbandModule, ""))
            sb.AppendLine("Runtime=" & CInt(WarbandSelectedRuntime))
            sb.AppendLine("NoIntro=" & If(WarbandNoIntro, "1", "0"))
            sb.AppendLine("ExtraArgs=" & If(WarbandExtraArgs, ""))
            Dim temp = WarbandSettingsFile & ".tmp"
            File.WriteAllText(temp, sb.ToString(), New UTF8Encoding(False))
            If File.Exists(WarbandSettingsFile) Then File.Delete(WarbandSettingsFile)
            File.Move(temp, WarbandSettingsFile)
        Catch ex As Exception
            Logger.Warn(ex, "保存战团设置失败")
        End Try
    End Sub

    ' ---------------- 定位游戏 ----------------

    ''' <summary>通过 Steam 注册表与 libraryfolders.vdf 定位 Warband 目录，找不到返回空字符串。</summary>
    Public Function WarbandDetectGamePath() As String
        Dim libraries As New List(Of String)
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        WarbandAddLibrary(libraries, seen, TryCast(Registry.GetValue("HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", Nothing), String))
        WarbandAddLibrary(libraries, seen, TryCast(Registry.GetValue("HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", Nothing), String))
        WarbandAddLibrary(libraries, seen, TryCast(Registry.GetValue("HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", Nothing), String))

        Dim candidates As New List(Of String)(libraries)
        For Each library In libraries.ToArray()
            Dim vdf = Path.Combine(library, "steamapps", "libraryfolders.vdf")
            If Not File.Exists(vdf) Then Continue For
            Try
                Dim text = File.ReadAllText(vdf)
                For Each m As Match In Regex.Matches(text, """path""\s*""([^""]+)""", RegexOptions.IgnoreCase)
                    candidates.Add(m.Groups(1).Value.Replace("\\\\", "\"))
                Next
                For Each m As Match In Regex.Matches(text, "^\s*""\d+""\s*""([^""]+)""", RegexOptions.Multiline)
                    candidates.Add(m.Groups(1).Value.Replace("\\", "\"))
                Next
            Catch ex As Exception
                Logger.Warn(ex, "解析 libraryfolders.vdf 失败")
            End Try
        Next

        For Each library In candidates
            Dim warbandPath = Path.Combine(library, "steamapps", "common", "MountBlade Warband")
            If Directory.Exists(warbandPath) Then Return warbandPath
        Next

        ' 兜底：扫常见盘符的一层 steamapps
        Try
            For Each drive In DriveInfo.GetDrives()
                If drive.DriveType <> DriveType.Fixed OrElse Not drive.IsReady Then Continue For
                For Each steamapps In Directory.GetDirectories(drive.RootDirectory.FullName, "steamapps", SearchOption.TopDirectoryOnly)
                    Dim warbandPath = Path.Combine(steamapps, "common", "MountBlade Warband")
                    If Directory.Exists(warbandPath) Then Return warbandPath
                Next
            Next
        Catch ex As Exception
            Logger.Warn(ex, "扫描盘符查找战团失败")
        End Try

        Return ""
    End Function

    ''' <summary>去重后登记一个 Steam 库根目录。</summary>
    Private Sub WarbandAddLibrary(libraries As List(Of String), seen As HashSet(Of String), rawPath As String)
        If String.IsNullOrWhiteSpace(rawPath) Then Return
        Dim path = rawPath.Trim().Trim(""""c)
        If path.Length > 0 AndAlso seen.Add(path) Then libraries.Add(path)
    End Sub

    Public Function WarbandExePath(gamePath As String, runtime As WarbandRuntime) As String
        If String.IsNullOrWhiteSpace(gamePath) Then Return ""
        Dim exeCandidate = Path.Combine(gamePath, RuntimeExe(runtime))
        Return If(File.Exists(exeCandidate), exeCandidate, "")
    End Function

    Public Function WarbandHasAnyExe(gamePath As String) As Boolean
        Return WarbandExePath(gamePath, WarbandRuntime.X86) <> "" OrElse WarbandExePath(gamePath, WarbandRuntime.X64) <> ""
    End Function

    ' ---------------- 模块扫描 ----------------

    ''' <summary>按文件夹名查找模块（找不到返回 Nothing）。启动时以它为准，避免界面状态串味。</summary>
    Public Function WarbandFindModule(gamePath As String, folderName As String) As WarbandModuleInfo
        If String.IsNullOrWhiteSpace(folderName) Then Return Nothing
        For Each modInfo In WarbandScanModules(gamePath)
            If modInfo.Folder.Equals(folderName, StringComparison.OrdinalIgnoreCase) Then Return modInfo
        Next
        Return Nothing
    End Function
    Public Function WarbandScanModules(gamePath As String) As List(Of WarbandModuleInfo)
        Dim result As New List(Of WarbandModuleInfo)
        If String.IsNullOrWhiteSpace(gamePath) Then Return result
        Dim modulesPath = Path.Combine(gamePath, "Modules")
        If Not Directory.Exists(modulesPath) Then Return result

        For Each moduleDir In Directory.GetDirectories(modulesPath)
            Dim info As New WarbandModuleInfo With {
                .Folder = Path.GetFileName(moduleDir),
                .FullPath = moduleDir
            }
            Dim iniPath = Path.Combine(moduleDir, "module.ini")
            info.HasModuleIni = File.Exists(iniPath)
            If info.HasModuleIni Then WarbandReadModuleIni(iniPath, info)
            WarbandMeasureResources(moduleDir, info)
            result.Add(info)
        Next

        result.Sort(Function(a, b) String.Compare(a.Folder, b.Folder, StringComparison.CurrentCultureIgnoreCase))
        Return result
    End Function

    Private Sub WarbandReadModuleIni(path As String, info As WarbandModuleInfo)
        Try
            For Each raw In File.ReadLines(path)
                Dim line = raw.Trim()
                If line.Length = 0 OrElse line(0) = "#"c OrElse line(0) = ";"c Then Continue For
                Dim index = line.IndexOf("="c)
                If index <= 0 Then Continue For
                Dim key = line.Substring(0, index).Trim().ToLowerInvariant()
                Dim value = WarbandUtil.StripInlineComment(line.Substring(index + 1))
                If value.Length = 0 Then Continue For
                Select Case key
                    Case "module_name" : info.InternalName = value
                    Case "compatible_with_warband", "compatible_mb", "compatible_module_version" : info.Compatible = value
                End Select
            Next
        Catch ex As Exception
            Logger.Warn(ex, $"解析 {path} 失败")
        End Try
    End Sub

    Private Sub WarbandMeasureResources(moduleDirectory As String, info As WarbandModuleInfo)
        Dim resourceDirectory = Path.Combine(moduleDirectory, "Resource")
        If Not Directory.Exists(resourceDirectory) Then Return

        Dim total As Long = 0
        Dim count As Integer = 0
        Try
            For Each file In Directory.EnumerateFiles(resourceDirectory, "*", SearchOption.AllDirectories)
                count += 1
                If count > 20000 Then Exit For
                Dim size As Long = 0
                Try
                    size = New FileInfo(file).Length
                Catch
                    Continue For
                End Try
                total += size
                If file.EndsWith(".brf", StringComparison.OrdinalIgnoreCase) AndAlso size >= 512L * 1024 * 1024 Then
                    info.BigFiles.Add(Path.GetFileName(file) & " " & WarbandUtil.FormatSize(size))
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, $"统计 {resourceDirectory} 体积失败")
        End Try

        info.ResourceSize = total
    End Sub

    ' ---------------- 命令行与启动 ----------------

    Public Function WarbandBuildArguments(modInfo As WarbandModuleInfo, runtime As WarbandRuntime, noIntro As Boolean, extraArgs As String) As String
        'WSE2 的命令行解析器不剥引号（--module "X" 会被当成带引号的目录名，找不到就回落到注册表里上次的模块），
        '而且它会把 --module 之后所有“不以 - 开头”的 token 都吃进模块名，
        '所以模块名必须是裸字符串、并且 --module 放在最后一项 —— 与 WSE2 官方启动器一致。
        Dim args As New List(Of String)
        If IsDedicated(runtime) Then
            args.Add("--config-path")
            args.Add("server_config.ini")
            args.Add("-r")
            args.Add(modInfo.Folder & ".txt")
        End If
        If noIntro AndAlso Not IsDedicated(runtime) Then args.Add("--no-intro")
        If Not String.IsNullOrWhiteSpace(extraArgs) Then args.Add(extraArgs.Trim())
        args.Add("--module")
        args.Add(modInfo.Folder)
        Return String.Join(" ", args)
    End Function

    ''' <summary>启动游戏，返回错误信息；成功返回空字符串。</summary>
    Public Function WarbandLaunch(gamePath As String, modInfo As WarbandModuleInfo, runtime As WarbandRuntime,
                                  noIntro As Boolean, extraArgs As String) As String
        If String.IsNullOrWhiteSpace(gamePath) Then Return "请先选择 Warband 游戏目录。"
        If modInfo Is Nothing Then Return "请先选择一个模块。"
        Dim exe = WarbandExePath(gamePath, runtime)
        If exe = "" Then Return $"游戏目录里没有 {RuntimeExe(runtime)}，请确认 WSE2 已安装。"

        Try
            '用整串 Arguments（而不是 ArgumentList）：后者会自动加引号，正好踩中 WSE2 不认引号的坑
            Dim psi As New ProcessStartInfo(exe) With {
                .WorkingDirectory = gamePath,
                .UseShellExecute = False,
                .Arguments = WarbandBuildArguments(modInfo, runtime, noIntro, extraArgs)
            }
            Process.Start(psi)
            WarbandRunning = True
            Logger.Info($"[Warband] 已启动 {Path.GetFileName(exe)}：{psi.Arguments}")
            Return ""
        Catch ex As Exception
            Logger.Error(ex, "[Warband] 启动失败")
            Return "启动失败：" & ex.Message
        End Try
    End Function

    ' ---------------- rgl_config.ini ----------------

    Public ReadOnly Property WarbandConfigDir As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount&Blade Warband WSE2")

    Public ReadOnly Property WarbandConfigPath As String = Path.Combine(WarbandConfigDir, "rgl_config.ini")

    ''' <summary>读取 rgl_config.ini 的所有行（文件不存在时返回空列表）。</summary>
    Public Function WarbandReadConfigLines() As List(Of String)
        Dim lines As New List(Of String)
        If Not File.Exists(WarbandConfigPath) Then Return lines
        Try
            lines.AddRange(File.ReadAllLines(WarbandConfigPath))
        Catch ex As Exception
            Logger.Warn(ex, "读取 rgl_config.ini 失败")
        End Try
        Return lines
    End Function

    Public Function WarbandGetConfig(lines As List(Of String), section As String, key As String, fallback As String) As String
        Dim inSection = False
        For Each raw In lines
            Dim line = raw.Trim()
            If line.Length > 2 AndAlso line(0) = "["c AndAlso line(line.Length - 1) = "]"c Then
                inSection = line.Substring(1, line.Length - 2).Trim().Equals(section, StringComparison.OrdinalIgnoreCase)
                Continue For
            End If
            If Not inSection Then Continue For
            Dim index = line.IndexOf("="c)
            If index <= 0 Then Continue For
            If line.Substring(0, index).Trim().Equals(key, StringComparison.OrdinalIgnoreCase) Then
                Return line.Substring(index + 1).Trim()
            End If
        Next
        Return fallback
    End Function

    Public Function WarbandSetConfig(lines As List(Of String), section As String, key As String, value As String) As Boolean
        Dim headerIndex = -1
        For i = 0 To lines.Count - 1
            Dim line = lines(i).Trim()
            If line.Length > 2 AndAlso line(0) = "["c AndAlso line(line.Length - 1) = "]"c AndAlso
               line.Substring(1, line.Length - 2).Trim().Equals(section, StringComparison.OrdinalIgnoreCase) Then
                headerIndex = i
                Exit For
            End If
        Next

        If headerIndex < 0 Then
            If lines.Count > 0 AndAlso lines(lines.Count - 1).Trim().Length > 0 Then lines.Add("")
            lines.Add("[" & section & "]")
            lines.Add(key & "=" & value)
            Return True
        End If

        Dim insertAt = headerIndex + 1
        For i = headerIndex + 1 To lines.Count - 1
            Dim line = lines(i).Trim()
            If line.Length > 2 AndAlso line(0) = "["c AndAlso line(line.Length - 1) = "]"c Then Exit For
            Dim index = line.IndexOf("="c)
            If index > 0 AndAlso line.Substring(0, index).Trim().Equals(key, StringComparison.OrdinalIgnoreCase) Then
                lines(i) = key & "=" & value
                Return True
            End If
            If line.Length > 0 Then insertAt = i + 1
        Next

        lines.Insert(insertAt, key & "=" & value)
        Return True
    End Function

    ''' <summary>备份后原子写入 rgl_config.ini，返回备份文件名（无备份返回空）。</summary>
    Public Function WarbandSaveConfig(lines As List(Of String)) As String
        Directory.CreateDirectory(WarbandConfigDir)
        Dim backup = ""
        If File.Exists(WarbandConfigPath) Then
            backup = Path.GetFileName(WarbandConfigPath & ".bak_" & Date.Now.ToString("yyyyMMdd_HHmmss"))
            File.Copy(WarbandConfigPath, Path.Combine(WarbandConfigDir, backup), True)
        End If
        Dim temp = WarbandConfigPath & ".pcl.tmp"
        File.WriteAllLines(temp, lines, New UTF8Encoding(False))
        If File.Exists(WarbandConfigPath) Then
            File.Replace(temp, WarbandConfigPath, Nothing, True)
        Else
            File.Move(temp, WarbandConfigPath)
        End If
        Return backup
    End Function

    ' ---------------- 体检与崩溃速诊 ----------------

    ''' <summary>启动前体检，返回人类可读的检查项（✓ / ! / ×）。</summary>
    Public Function WarbandPreflight(gamePath As String, modInfo As WarbandModuleInfo, runtime As WarbandRuntime) As List(Of String)
        Dim items As New List(Of String)
        If String.IsNullOrWhiteSpace(gamePath) OrElse Not Directory.Exists(gamePath) Then
            items.Add("× 游戏目录：未设置或不存在")
            Return items
        End If

        items.Add("✓ 游戏目录：" & gamePath)
        items.Add(If(WarbandExePath(gamePath, runtime) <> "", 
                    $"✓ 运行时：{RuntimeExe(runtime)} 已就绪", 
                    $"× 运行时：缺少 {RuntimeExe(runtime)}"))

        Dim appIdFile = Path.Combine(gamePath, "steam_appid.txt")
        Dim appId = If(File.Exists(appIdFile), File.ReadAllText(appIdFile).Trim(), "")
        items.Add(If(appId = WarbandSteamAppId,
                    "✓ steam_appid.txt：48700",
                    $"! steam_appid.txt：{(If(appId = "", "缺失", appId))}（Warband 应为 48700）"))

        Dim hasSteam = File.Exists(Path.Combine(gamePath, "steam_api_wse2.dll"))
        Dim hasFmod = File.Exists(Path.Combine(gamePath, "fmodex_wse2.dll"))
        items.Add(If(hasSteam AndAlso hasFmod,
                    "✓ WSE2 依赖：steam_api_wse2.dll / fmodex_wse2.dll 均存在",
                    "! WSE2 依赖：缺少 " & If(hasSteam, "", "steam_api_wse2.dll ") & If(hasFmod, "", "fmodex_wse2.dll")))

        If modInfo IsNot Nothing Then
            items.Add(If(modInfo.HasModuleIni,
                        $"✓ 模块：{modInfo.Folder}（{modInfo.ResourceText}）",
                        $"! 模块：{modInfo.Folder} 下没有 module.ini，可能不是有效模块"))
            If Is32Bit(runtime) AndAlso modInfo.ResourceSize >= 1200L * 1024 * 1024 Then
                items.Add($"! 32 位内存风险：该模块 Resource 已达 {modInfo.ResourceText}，2GB 地址空间下容易以 0xE0000006 崩溃，建议改用 x64")
            End If
            If modInfo.BigFiles.Count > 0 Then
                items.Add("! 可疑资源包（≥512MB，常见的打包错误）：" & String.Join("，", modInfo.BigFiles))
            End If
        End If

        items.Add(If(File.Exists(WarbandConfigPath),
                    "✓ WSE2 配置：" & WarbandConfigPath,
                    "! WSE2 配置：未找到 rgl_config.ini（先运行一次游戏会生成）"))

        Return items
    End Function

    Public Function WarbandReadLogTail(gamePath As String, maxLines As Integer) As String
        Dim logPath = Path.Combine(gamePath, "rgl_log.txt")
        If Not File.Exists(logPath) Then Return "rgl_log.txt 还不存在（还没有运行过游戏）。"
        Try
            Dim buffer As New Queue(Of String)()
            Using stream As New FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Using reader As New StreamReader(stream)
                    Do While reader.Peek() >= 0
                        buffer.Enqueue(reader.ReadLine())
                        If buffer.Count > maxLines Then buffer.Dequeue()
                    Loop
                End Using
            End Using
            Return String.Join(Environment.NewLine, buffer)
        Catch ex As Exception
            Return "读取日志失败：" & ex.Message
        End Try
    End Function

    ''' <summary>按已知崩溃特征给出结论。</summary>
    Public Function WarbandDiagnose(gamePath As String) As String
        Dim tail = WarbandReadLogTail(gamePath, 400)

        If tail.Contains("0xE0000006") OrElse tail.Contains("Unable to load resource file") OrElse tail.Contains("out of memory") Then
            Return "【致命】内存耗尽（32 位地址空间打满）" & vbCrLf &
                   "建议：改用 x64 构建；把纹理质量/阴影贴图降到 2/1024 以下；排查模块 Resource 里的巨型 .brf。"
        End If
        If tail.Contains("Unable to find face mesh") OrElse tail.Contains("Unable to find LOD mesh") OrElse tail.Contains("male_head_new") Then
            Return "【致命】模块资源缺失（面部/头部网格）" & vbCrLf &
                   "建议：用 Steam 验证文件完整性或重新解压该模块；WSE2 会把缺失网格直接升级成致命错误。"
        End If
        If tail.Contains("dispatchAllCollisionPairs") OrElse tail.Contains("btCollisionDispatcher") Then
            Return "【警告】物理多线程碰撞访问违例（x64 构建已知问题）" & vbCrLf &
                   "建议：调低尸体/布娃娃上限，减少物理特效。"
        End If
        If tail.Contains("ui_module_newer_than_application") OrElse tail.Contains("Invalid operation code") Then
            Return "【警告】模块与 WSE2 版本不匹配" & vbCrLf &
                   "建议：升级 WSE2 或换用与本体版本匹配的模块。"
        End If
        Return "未命中已知崩溃特征。可以把 Logs 目录下的 rgl_log_*_crash.txt 反馈给项目 issue。"
    End Function

End Module

''' <summary>下载库里的一条 mod 记录（存在 %APPDATA%\PCL Warband\mods.json，可手动编辑）。</summary>
Public Class WarbandModEntry
    Public Property Name As String = ""
    Public Property Note As String = ""
    Public Property ChineseSearchUrl As String = ""
    Public Property PanUrl As String = ""
    Public Property PanCode As String = ""
End Class

Public Module ModWarbandLibrary

    Public ReadOnly Property WarbandModLibraryFile As String = Path.Combine(WarbandDataDir, "mods.json")

    ''' <summary>读取下载库；文件不存在时用本机 Modules 生成一份并落盘。</summary>
    Public Function WarbandLoadModLibrary() As List(Of WarbandModEntry)
        If File.Exists(WarbandModLibraryFile) Then
            Try
                Dim loaded = JsonConvert.DeserializeObject(Of List(Of WarbandModEntry))(File.ReadAllText(WarbandModLibraryFile))
                If loaded IsNot Nothing AndAlso loaded.Count > 0 Then Return loaded
            Catch ex As Exception
                Logger.Warn(ex, "[Warband] 读取 mods.json 失败，将重新生成")
            End Try
        End If

        Dim generated = WarbandBuildDefaultModLibrary()
        WarbandSaveModLibrary(generated)
        Return generated
    End Function

    Public Sub WarbandSaveModLibrary(entries As List(Of WarbandModEntry))
        Try
            Directory.CreateDirectory(WarbandDataDir)
            File.WriteAllText(WarbandModLibraryFile, JsonConvert.SerializeObject(entries, Formatting.Indented), New UTF8Encoding(False))
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 保存 mods.json 失败")
        End Try
    End Sub

    ''' <summary>用已安装模块生成条目：默认只带中文站搜索链接，网盘地址留给用户自己填。</summary>
    Private Function WarbandBuildDefaultModLibrary() As List(Of WarbandModEntry)
        Dim entries As New List(Of WarbandModEntry)

        For Each modInfo In WarbandScanModules(WarbandGamePath)
            entries.Add(New WarbandModEntry With {
                .Name = modInfo.Folder,
                .Note = If(modInfo.Compatible = "", "已安装 · " & modInfo.ResourceText, "已安装 · 兼容 " & modInfo.Compatible & " · " & modInfo.ResourceText),
                .ChineseSearchUrl = WarbandChineseSearchUrl(modInfo.Folder)
            })
        Next

        If entries.Count = 0 Then
            Dim classics = {"Native", "Prophesy of Pendor", "In the Name of Jerusalem", "The Last Days of the Third Age",
                            "Viking Conquest", "Napoleonic Wars", "Solid and Shade", "1257 AD"}
            For Each modName In classics
                entries.Add(New WarbandModEntry With {
                    .Name = modName,
                    .Note = "未安装",
                    .ChineseSearchUrl = WarbandChineseSearchUrl(modName)
                })
            Next
        End If

        Return entries
    End Function

    ''' <summary>中文站（骑马与砍杀中文站 bbs.mountblade.com.cn）站内搜索链接。</summary>
    Public Function WarbandChineseSearchUrl(modName As String) As String
        Return "https://www.baidu.com/s?wd=" & Uri.EscapeDataString("site:bbs.mountblade.com.cn " & modName)
    End Function

    Public Const WarbandChineseSite As String = "https://bbs.mountblade.com.cn/"

    Public Sub WarbandOpenUrl(url As String)
        If String.IsNullOrWhiteSpace(url) Then Return
        Try
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 打开链接失败：" & url)
        End Try
    End Sub

End Module