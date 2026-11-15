' PCL 战团版 —— 内置 WSE2 运行时
'
' 目的：玩家不用自己去 WSE2 官网/网盘找安装包，下载本启动器后点一次「一键安装 WSE2」即可。
' 载荷（4 个游戏 exe + steam_api_wse2.dll + fmodex_wse2.dll）以 zip 形式嵌在 exe 里，
' 安装时解到游戏根目录；已存在的同名文件会先备份成 *.bak_pwl_时间戳。
' 另外支持「从本地 zip 安装」，方便以后换 WSE2 版本而不必重新编译启动器。

Imports System.IO
Imports System.Reflection
Imports System.Text

Public Module ModWarbandRuntime

    ''' <summary>内置载荷的资源名（与 vbproj 里的 LogicalName 一致）。</summary>
    Private Const PayloadResourceName As String = "PCL.WarbandWse2Payload.zip"

    Public ReadOnly WarbandWse2Files As String() = {
        "mb_warband_wse2.exe",
        "mb_warband_wse2_x64.exe",
        "mb_warband_wse2_dedicated.exe",
        "mb_warband_wse2_dedicated_x64.exe",
        "steam_api_wse2.dll",
        "fmodex_wse2.dll"
    }

    ''' <summary>启动器里是否带了 WSE2 载荷。</summary>
    Public Function WarbandHasBundledWse2() As Boolean
        Try
            Using stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
                Return stream IsNot Nothing
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>返回游戏目录里缺失的 WSE2 文件（空列表=已齐全）。</summary>
    Public Function WarbandWse2Missing(gamePath As String) As List(Of String)
        Dim missing As New List(Of String)
        If String.IsNullOrWhiteSpace(gamePath) OrElse Not Directory.Exists(gamePath) Then Return missing
        For Each fileName In WarbandWse2Files
            If Not File.Exists(Path.Combine(gamePath, fileName)) Then missing.Add(fileName)
        Next
        Return missing
    End Function

    Public Function WarbandWse2StatusText(gamePath As String) As String
        If String.IsNullOrWhiteSpace(gamePath) OrElse Not Directory.Exists(gamePath) Then Return "未设置游戏目录"
        Dim missing = WarbandWse2Missing(gamePath)
        If missing.Count = 0 Then Return "WSE2 已就绪（" & WarbandWse2Files.Length & " 个文件齐全）"
        Return $"缺少 {missing.Count} 个文件：{String.Join("、", missing)}"
    End Function

    ''' <summary>把内置载荷安装到游戏目录；返回给人看的报告。</summary>
    Public Function WarbandInstallBundledWse2(gamePath As String) As String
        If String.IsNullOrWhiteSpace(gamePath) OrElse Not Directory.Exists(gamePath) Then Return "游戏目录不存在。"
        If Not WarbandHasBundledWse2() Then Return "这个启动器包里没有内置 WSE2，请用「从本地 zip 安装…」。"
        Try
            Using stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
                Using ms As New MemoryStream()
                    stream.CopyTo(ms)
                    Return WarbandInstallWse2FromBytes(ms.ToArray(), gamePath)
                End Using
            End Using
        Catch ex As Exception
            Logger.Error(ex, "[Warband] 安装内置 WSE2 失败")
            Return "安装失败：" & ex.Message
        End Try
    End Function

    ''' <summary>从本地 zip 安装/覆盖 WSE2。</summary>
    Public Function WarbandInstallWse2FromZip(zipPath As String, gamePath As String) As String
        If Not File.Exists(zipPath) Then Return "找不到 zip 文件。"
        If String.IsNullOrWhiteSpace(gamePath) OrElse Not Directory.Exists(gamePath) Then Return "游戏目录不存在。"
        Try
            Return WarbandInstallWse2FromBytes(File.ReadAllBytes(zipPath), gamePath)
        Catch ex As Exception
            Logger.Error(ex, "[Warband] 从 zip 安装 WSE2 失败")
            Return "安装失败：" & ex.Message
        End Try
    End Function

    Private Function WarbandInstallWse2FromBytes(payload As Byte(), gamePath As String) As String
        Dim tempRoot = Path.Combine(Path.GetTempPath(), "PCL-Warband-WSE2-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempRoot)
        Dim tempZip = Path.Combine(tempRoot, "payload.zip")
        Dim tempDir = Path.Combine(tempRoot, "x")
        Directory.CreateDirectory(tempDir)

        Try
            File.WriteAllBytes(tempZip, payload)
            IO.Compression.ZipFile.ExtractToDirectory(tempZip, tempDir)

            Dim stamp = Date.Now.ToString("yyyyMMdd_HHmmss")
            Dim installed As New List(Of String)
            Dim backedUp As New List(Of String)

            For Each sourceFile In Directory.GetFiles(tempDir)
                Dim fileName = Path.GetFileName(sourceFile)
                Dim target = Path.Combine(gamePath, fileName)
                If File.Exists(target) Then
                    Dim backup = target & ".bak_pwl_" & stamp
                    File.Copy(target, backup, True)
                    backedUp.Add(fileName)
                End If
                File.Copy(sourceFile, target, True)
                installed.Add(fileName)
            Next

            Dim report As New StringBuilder()
            report.AppendLine($"已安装 {installed.Count} 个 WSE2 文件到：{gamePath}")
            If backedUp.Count > 0 Then report.AppendLine("原有文件已备份（" & stamp & "）：" & String.Join("、", backedUp))
            report.AppendLine("仍然缺：" & If(WarbandWse2Missing(gamePath).Count = 0, "无", String.Join("、", WarbandWse2Missing(gamePath))))
            Logger.Info("[Warband] WSE2 安装完成：" & String.Join(",", installed))
            Return report.ToString()
        Catch ex As Exception
            Logger.Error(ex, "[Warband] 释放 WSE2 载荷失败")
            Return "安装失败：" & ex.Message
        Finally
            Try
                Directory.Delete(tempRoot, True)
            Catch
            End Try
        End Try
    End Function

End Module
