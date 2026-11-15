' PCL 战团版 —— 断点续传 / 多线程下载器
'
' 设计要点：
'  · 探测（HEAD，失败则 GET Range:0-0）拿到文件大小与是否支持 Range；
'  · 支持 Range 时按线程数切片，每片写自己的 .part 文件，天然可断点续传；
'  · 分片信息落 <文件>.pwl.json，重启启动器后仍能接着下；
'  · 不支持 Range 时退化为单流下载（依然可暂停/继续，但进度从 0 重来）；
'  · 全部完成后合并分片 → 可选自动解压到 Modules。

Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Public Enum WarbandDownloadState
    Waiting = 0
    Running = 1
    Paused = 2
    Merging = 3
    Completed = 4
    Failed = 5
    Canceled = 6
End Enum

Public Class WarbandDownloadTask
    Public Property Url As String = ""
    Public Property FileName As String = ""
    Public Property SavePath As String = ""
    Public Property Threads As Integer = 8
    Public Property Unzip As Boolean
    Public Property UnzipTo As String = ""

    Public Property State As WarbandDownloadState = WarbandDownloadState.Waiting
    Public Property TotalBytes As Long
    Public Property DownloadedBytes As Long
    Public Property SpeedBytesPerSec As Long
    Public Property ErrorText As String = ""
    Public Property MessageText As String = ""

    Friend Property PauseRequested As Boolean
    Friend Property CancelRequested As Boolean

    Public ReadOnly Property IsFinished As Boolean
        Get
            Return State = WarbandDownloadState.Completed OrElse State = WarbandDownloadState.Failed OrElse State = WarbandDownloadState.Canceled
        End Get
    End Property

    Public ReadOnly Property IsActive As Boolean
        Get
            Return State = WarbandDownloadState.Waiting OrElse State = WarbandDownloadState.Running OrElse State = WarbandDownloadState.Paused OrElse State = WarbandDownloadState.Merging
        End Get
    End Property

    Public ReadOnly Property Progress01 As Double
        Get
            If TotalBytes <= 0 Then Return 0
            Return Math.Min(1, DownloadedBytes / TotalBytes)
        End Get
    End Property

    Public ReadOnly Property ProgressText As String
        Get
            If TotalBytes > 0 Then
                Return $"{WarbandUtil.FormatSize(DownloadedBytes)} / {WarbandUtil.FormatSize(TotalBytes)}"
            End If
            Return WarbandUtil.FormatSize(DownloadedBytes)
        End Get
    End Property

    Public ReadOnly Property SpeedText As String
        Get
            If State <> WarbandDownloadState.Running OrElse SpeedBytesPerSec <= 0 Then Return ""
            Return WarbandUtil.FormatSize(SpeedBytesPerSec) & "/s"
        End Get
    End Property

    Public ReadOnly Property EtaText As String
        Get
            If State <> WarbandDownloadState.Running OrElse SpeedBytesPerSec <= 0 OrElse TotalBytes <= 0 Then Return ""
            Dim remain = Math.Max(0, TotalBytes - DownloadedBytes)
            Dim seconds = CLng(remain / Math.Max(1, SpeedBytesPerSec))
            If seconds >= 3600 Then Return $"{seconds \ 3600}h{(seconds Mod 3600) \ 60}m"
            If seconds >= 60 Then Return $"{seconds \ 60}m{seconds Mod 60}s"
            Return $"{seconds}s"
        End Get
    End Property

    Public ReadOnly Property StateText As String
        Get
            Select Case State
                Case WarbandDownloadState.Waiting : Return "等待中"
                Case WarbandDownloadState.Running : Return "下载中"
                Case WarbandDownloadState.Paused : Return "已暂停"
                Case WarbandDownloadState.Merging : Return "合并分片…"
                Case WarbandDownloadState.Completed : Return "已完成"
                Case WarbandDownloadState.Failed : Return "失败：" & ErrorText
                Case WarbandDownloadState.Canceled : Return "已取消"
                Case Else : Return ""
            End Select
        End Get
    End Property
End Class

Public Module ModWarbandDownloader

    Private Const TempSuffix As String = ".pwl.json"
    Private ReadOnly Engine As New WarbandDownloadEngine()

    ''' <summary>创建一个任务并开始下载（不阻塞调用方）。</summary>
    Public Function WarbandStartDownload(url As String, fileName As String, savePath As String, threads As Integer,
                                         unzip As Boolean, unzipTo As String) As WarbandDownloadTask
        Dim task As New WarbandDownloadTask With {
            .Url = url.Trim(),
            .FileName = fileName,
            .SavePath = savePath,
            .Threads = Math.Max(1, Math.Min(16, threads)),
            .Unzip = unzip,
            .UnzipTo = If(unzipTo, "")
        }
        task.State = WarbandDownloadState.Waiting
        Engine.Start(task)
        Return task
    End Function

    Public Sub WarbandPauseDownload(task As WarbandDownloadTask)
        If task Is Nothing Then Return
        task.PauseRequested = True
    End Sub

    Public Sub WarbandResumeDownload(task As WarbandDownloadTask)
        If task Is Nothing Then Return
        If task.State <> WarbandDownloadState.Paused AndAlso task.State <> WarbandDownloadState.Failed Then Return
        task.PauseRequested = False
        task.CancelRequested = False
        task.ErrorText = ""
        task.State = WarbandDownloadState.Waiting
        Engine.Start(task)
    End Sub

    Public Sub WarbandCancelDownload(task As WarbandDownloadTask)
        If task Is Nothing Then Return
        task.CancelRequested = True
        task.PauseRequested = False
        Dim savePath = task.SavePath
        System.Threading.Tasks.Task.Run(Sub()
                     Thread.Sleep(300)
                     Try
                         If File.Exists(savePath) Then File.Delete(savePath)
                         For Each part In WarbandPartFiles(savePath)
                             If File.Exists(part) Then File.Delete(part)
                         Next
                         Dim meta = savePath & TempSuffix
                         If File.Exists(meta) Then File.Delete(meta)
                     Catch ex As Exception
                         Logger.Warn(ex, "[Warband] 清理下载分片失败")
                     End Try
                 End Sub)
        task.State = WarbandDownloadState.Canceled
    End Sub

    ''' <summary>从 URL 猜文件名（公开给 CLI 用）。</summary>
    Public Function WarbandGuessFileName(url As String) As String
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
    ''' <summary>把常见的网盘分享页认出来，好提示用户需要直链。</summary>
    Public Function WarbandNetdiskName(url As String) As String
        If String.IsNullOrWhiteSpace(url) Then Return ""
        Dim lower = url.ToLowerInvariant()
        If lower.Contains("pan.baidu.com") Then Return "百度网盘"
        If lower.Contains("123pan.com") OrElse lower.Contains("123684.com") Then Return "123 网盘"
        If lower.Contains("pan.quark.cn") Then Return "夸克网盘"
        If lower.Contains("aliyundrive.com") OrElse lower.Contains("alipan.com") Then Return "阿里云盘"
        If lower.Contains("lanzou") Then Return "蓝奏云"
        If lower.Contains("cloud.189.cn") Then Return "天翼云盘"
        If lower.Contains("caiyun.139.com") Then Return "移动云盘"
        Return ""
    End Function

    Friend Function WarbandPartFiles(savePath As String) As IEnumerable(Of String)
        Dim folder = Path.GetDirectoryName(savePath)
        If String.IsNullOrEmpty(folder) OrElse Not Directory.Exists(folder) Then Return Array.Empty(Of String)()
        Dim prefix = Path.GetFileName(savePath) & ".part"
        Return Directory.GetFiles(folder, prefix & "*")
    End Function

End Module

''' <summary>下载引擎：探测 → 分片 → 并发下载 → 合并 → 可选解压。</summary>
Friend Class WarbandDownloadEngine

    Private Shared _tlsReady As Boolean

    Private Shared Sub EnsureTls()
        If _tlsReady Then Return
        _tlsReady = True
        Try
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 Or SecurityProtocolType.Tls11
            ServicePointManager.DefaultConnectionLimit = 32
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 初始化 TLS 失败")
        End Try
    End Sub

    Public Sub Start(task As WarbandDownloadTask)
        EnsureTls()
        System.Threading.Tasks.Task.Run(Sub()
                     Try
                         RunTask(task)
                     Catch ex As Exception
                         task.ErrorText = ex.Message
                         task.State = WarbandDownloadState.Failed
                         Logger.Error(ex, "[Warband] 下载失败：" & task.Url)
                     End Try
                 End Sub)
    End Sub

    Private Sub RunTask(task As WarbandDownloadTask)
        If String.IsNullOrWhiteSpace(task.SavePath) Then Throw New ArgumentException("保存路径为空")

        Dim folder = Path.GetDirectoryName(task.SavePath)
        If Not String.IsNullOrEmpty(folder) Then Directory.CreateDirectory(folder)

        Dim metaPath = task.SavePath & ".pwl.json"
        Dim meta As WarbandDownloadMeta = Nothing

        '续传：有元数据就接着用
        If File.Exists(metaPath) Then
            Try
                meta = JsonConvert.DeserializeObject(Of WarbandDownloadMeta)(File.ReadAllText(metaPath))
            Catch ex As Exception
                Logger.Warn(ex, "[Warband] 读取分片元数据失败，将重新开始")
                meta = Nothing
            End Try
        End If

        If meta Is Nothing OrElse meta.Url <> task.Url Then
            meta = Nothing
            Probe(task)
        Else
            task.TotalBytes = meta.TotalBytes
        End If

        Dim supportsRange = task.TotalBytes > 0 AndAlso meta IsNot Nothing AndAlso meta.Segments IsNot Nothing AndAlso meta.Segments.Count > 0
        If meta Is Nothing Then
            '第一次：探测后切片
            Dim probed = Probe(task)
            If probed AndAlso task.TotalBytes >= 4 * 1024 * 1024 Then
                meta = BuildMeta(task)
            Else
                meta = Nothing
            End If
        End If

        If meta IsNot Nothing Then
            SaveMeta(metaPath, meta)
            DownloadSegmented(task, meta, metaPath)
        Else
            DownloadSingle(task)
        End If

        If task.CancelRequested OrElse task.PauseRequested Then Return

        task.State = WarbandDownloadState.Merging
        task.MessageText = "合并分片…"
        MergeParts(task)

        Dim metaFile = task.SavePath & ".pwl.json"
        If File.Exists(metaFile) Then File.Delete(metaFile)

        task.DownloadedBytes = task.TotalBytes
        task.State = WarbandDownloadState.Completed
        task.MessageText = "已完成"

        If task.Unzip AndAlso task.SavePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) AndAlso task.UnzipTo <> "" Then
            Try
                task.MessageText = "解压中…"
                Directory.CreateDirectory(task.UnzipTo)
                WarbandExtractZip(task.SavePath, task.UnzipTo)
                task.MessageText = "已完成并解压到 " & task.UnzipTo
            Catch ex As Exception
                task.MessageText = "已下载，但解压失败：" & ex.Message
                Logger.Warn(ex, "[Warband] 解压失败")
            End Try
        End If
    End Sub

    ''' <summary>探测大小与 Range 支持；返回是否支持分段。</summary>
    Private Function Probe(task As WarbandDownloadTask) As Boolean
        'HEAD
        Try
            Dim head = CType(WebRequest.Create(task.Url), HttpWebRequest)
            head.Method = "HEAD"
            head.UserAgent = "PCL-Warband/0.1"
            head.Timeout = 15000
            head.AllowAutoRedirect = True
            Using resp = DirectCast(head.GetResponse(), HttpWebResponse)
                task.TotalBytes = resp.ContentLength
                Dim accept = If(resp.Headers("Accept-Ranges"), "")
                If task.TotalBytes > 0 AndAlso accept.ToLowerInvariant().Contains("bytes") Then Return True
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] HEAD 探测失败，改用 GET Range 探测")
        End Try

        'GET Range: bytes=0-0
        Try
            Dim getReq = CType(WebRequest.Create(task.Url), HttpWebRequest)
            getReq.Method = "GET"
            getReq.UserAgent = "PCL-Warband/0.1"
            getReq.Timeout = 15000
            getReq.AllowAutoRedirect = True
            getReq.AddRange(0, 0)
            Using resp = DirectCast(getReq.GetResponse(), HttpWebResponse)
                Dim contentRange = If(resp.Headers("Content-Range"), "")
                If contentRange.Contains("/") Then
                    Dim totalText = contentRange.Substring(contentRange.LastIndexOf("/"c) + 1)
                    Dim total As Long
                    If Long.TryParse(totalText, total) Then
                        task.TotalBytes = total
                        Return resp.StatusCode = HttpStatusCode.PartialContent
                    End If
                End If
                If task.TotalBytes <= 0 Then task.TotalBytes = resp.ContentLength
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] GET 探测失败")
        End Try

        Return False
    End Function

    Private Function BuildMeta(task As WarbandDownloadTask) As WarbandDownloadMeta
        Dim meta As New WarbandDownloadMeta With {
            .Url = task.Url,
            .TotalBytes = task.TotalBytes,
            .Segments = New List(Of WarbandDownloadSegment)()
        }
        Dim count = Math.Max(1, Math.Min(task.Threads, CInt(Math.Ceiling(task.TotalBytes / (1024.0 * 1024.0)))))
        Dim size = task.TotalBytes \ count
        For i = 0 To count - 1
            Dim startAt = i * size
            Dim endAt = If(i = count - 1, task.TotalBytes - 1, (i + 1) * size - 1)
            meta.Segments.Add(New WarbandDownloadSegment With {.Start = startAt, .End = endAt, .Done = 0})
        Next
        Return meta
    End Function

    Private Sub SaveMeta(metaPath As String, meta As WarbandDownloadMeta)
        Try
            File.WriteAllText(metaPath, JsonConvert.SerializeObject(meta), New UTF8Encoding(False))
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 写入分片元数据失败")
        End Try
    End Sub

    Private Function PartPath(savePath As String, index As Integer) As String
        Return savePath & ".part" & index
    End Function

    Private Sub DownloadSegmented(task As WarbandDownloadTask, meta As WarbandDownloadMeta, metaPath As String)
        task.State = WarbandDownloadState.Running
        Dim totalDone As Long = meta.Segments.Sum(Function(s) s.Done)
        Interlocked.Exchange(task.DownloadedBytes, totalDone)

        Dim workers As New List(Of Task)
        For index = 0 To meta.Segments.Count - 1
            Dim segIndex = index
            Dim segment = meta.Segments(segIndex)
            workers.Add(System.Threading.Tasks.Task.Run(Sub() DownloadSegment(task, meta, segIndex, metaPath)))
        Next
        Try
            System.Threading.Tasks.Task.WaitAll(workers.ToArray())
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 分片下载异常")
        End Try
    End Sub

    Private Sub DownloadSegment(task As WarbandDownloadTask, meta As WarbandDownloadMeta, segIndex As Integer, metaPath As String)
        Dim segment = meta.Segments(segIndex)
        If segment.Done >= (segment.End - segment.Start + 1) Then Return

        Dim partFile = PartPath(task.SavePath, segIndex)
        Dim existing As Long = 0
        If File.Exists(partFile) Then existing = New FileInfo(partFile).Length
        If existing > segment.Done Then segment.Done = existing

        Dim from = segment.Start + segment.Done
        If from > segment.End Then Return

        Dim attempt = 0
        While attempt < 5
            If task.CancelRequested OrElse task.PauseRequested Then Return
            attempt += 1
            Try
                Dim req = CType(WebRequest.Create(task.Url), HttpWebRequest)
                req.Method = "GET"
                req.UserAgent = "PCL-Warband/0.1"
                req.Timeout = 20000
                req.ReadWriteTimeout = 30000
                req.AllowAutoRedirect = True
                req.AddRange(from, segment.End)

                Using resp = DirectCast(req.GetResponse(), HttpWebResponse)
                    Using stream = resp.GetResponseStream()
                        Using fs As New FileStream(partFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 81920)
                            Dim buffer(81919) As Byte
                            While True
                                If task.CancelRequested OrElse task.PauseRequested Then
                                    SaveMeta(metaPath, meta)
                                    Return
                                End If
                                Dim read = stream.Read(buffer, 0, buffer.Length)
                                If read <= 0 Then Exit While
                                fs.Write(buffer, 0, read)
                                segment.Done += read
                                Interlocked.Add(task.DownloadedBytes, read)
                                from += read
                            End While
                        End Using
                    End Using
                End Using

                SaveMeta(metaPath, meta)
                Return
            Catch ex As Exception
                task.ErrorText = ex.Message
                Logger.Warn(ex, $"[Warband] 分片 {segIndex} 第 {attempt} 次失败：{ex.Message}")
                Thread.Sleep(800 * attempt)
                '重试前按真实文件长度校正进度
                If File.Exists(partFile) Then
                    Dim realLen = New FileInfo(partFile).Length
                    If realLen > segment.Done Then
                        Interlocked.Add(task.DownloadedBytes, realLen - segment.Done)
                        segment.Done = realLen
                    End If
                    from = segment.Start + segment.Done
                End If
            End Try
        End While

        If Not task.CancelRequested AndAlso Not task.PauseRequested Then
            task.State = WarbandDownloadState.Failed
            task.ErrorText = "分片 " & segIndex & " 多次重试失败"
        End If
    End Sub

    Private Sub DownloadSingle(task As WarbandDownloadTask)
        task.State = WarbandDownloadState.Running
        Dim tempPath = task.SavePath & ".part0"
        Dim req = CType(WebRequest.Create(task.Url), HttpWebRequest)
        req.Method = "GET"
        req.UserAgent = "PCL-Warband/0.1"
        req.Timeout = 20000
        req.ReadWriteTimeout = 30000
        req.AllowAutoRedirect = True

        Using resp = DirectCast(req.GetResponse(), HttpWebResponse)
            If task.TotalBytes <= 0 Then task.TotalBytes = resp.ContentLength
            Using stream = resp.GetResponseStream()
                Using fs As New FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 81920)
                    Dim buffer(81919) As Byte
                    While True
                        If task.CancelRequested OrElse task.PauseRequested Then Return
                        Dim read = stream.Read(buffer, 0, buffer.Length)
                        If read <= 0 Then Exit While
                        fs.Write(buffer, 0, read)
                        Interlocked.Add(task.DownloadedBytes, read)
                    End While
                End Using
            End Using
        End Using
    End Sub


    ''' <summary>解压 zip 到目录（覆盖已存在文件；net48 没有 overwrite 重载）。</summary>
    Private Shared Sub WarbandExtractZip(zipPath As String, targetDir As String)
        Using archive = IO.Compression.ZipFile.OpenRead(zipPath)
            For Each entry In archive.Entries
                If String.IsNullOrEmpty(entry.Name) Then Continue For
                Dim destPath = Path.Combine(targetDir, entry.FullName)
                Dim destDir = Path.GetDirectoryName(destPath)
                If Not String.IsNullOrEmpty(destDir) Then Directory.CreateDirectory(destDir)
                entry.ExtractToFile(destPath, True)
            Next
        End Using
    End Sub
    Private Shared Function WarbandPartIndex(partPath As String) As Integer
        Dim m = System.Text.RegularExpressions.Regex.Match(partPath, "\.part(\d+)$")
        If m.Success Then Return Integer.Parse(m.Groups(1).Value)
        Return 0
    End Function

    Private Sub MergeParts(task As WarbandDownloadTask)
        Dim parts = WarbandPartFiles(task.SavePath).OrderBy(Function(partPath) WarbandPartIndex(partPath)).ToList()
        If parts.Count = 0 Then
            '单流下载：直接把 part0 改名为目标
            Dim singlePart = task.SavePath & ".part0"
            If File.Exists(singlePart) Then
                If File.Exists(task.SavePath) Then File.Delete(task.SavePath)
                File.Move(singlePart, task.SavePath)
            End If
            Return
        End If

        Dim temp = task.SavePath & ".merging"
        Using output As New FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920)
            For Each part In parts
                Using input As New FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 81920)
                    input.CopyTo(output)
                End Using
            Next
        End Using
        If File.Exists(task.SavePath) Then File.Delete(task.SavePath)
        File.Move(temp, task.SavePath)
        For Each part In parts
            Try
                File.Delete(part)
            Catch
            End Try
        Next
    End Sub
End Class

Friend Class WarbandDownloadMeta
    Public Property Url As String = ""
    Public Property TotalBytes As Long
    Public Property Segments As List(Of WarbandDownloadSegment)
End Class

Friend Class WarbandDownloadSegment
    Public Property Start As Long
    Public Property [End] As Long
    Public Property Done As Long
End Class