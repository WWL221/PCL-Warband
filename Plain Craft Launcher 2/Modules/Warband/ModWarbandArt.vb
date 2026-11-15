' PCL 战团版 —— 模块预览图（仿 WSE2 官方启动器的模块配图）
'
' 取图顺序：
'   1. 用户自定义：%APPDATA%\PCL Warband\previews\<模块文件夹名>.<png|jpg|jpeg|webp|bmp>
'   2. 模块自带：Modules\<模块>\preview|module_preview|ModulePreview|art|logo|main.<扩展名>
'   3. 都没有 → 界面显示占位卡片，可点「更换图片」自己选一张。

Imports System.IO

Public Module ModWarbandArt

    Public ReadOnly Property WarbandPreviewDir As String = Path.Combine(WarbandDataDir, "previews")

    Private ReadOnly ImageExtensions As String() = {".png", ".jpg", ".jpeg", ".webp", ".bmp"}

    Private ReadOnly ModuleImageNames As String() = {"preview", "module_preview", "ModulePreview", "art", "logo", "main"}

    ''' <summary>找当前模块该显示哪张图；找不到返回空字符串。</summary>
    Public Function WarbandFindModuleArt(modFolder As String, gamePath As String) As String
        If String.IsNullOrWhiteSpace(modFolder) Then Return ""

        '1. 用户自定义
        Try
            Directory.CreateDirectory(WarbandPreviewDir)
            For Each ext In ImageExtensions
                Dim customPath = Path.Combine(WarbandPreviewDir, modFolder & ext)
                If File.Exists(customPath) Then Return customPath
            Next
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 读取自定义预览图目录失败")
        End Try

        '2. 模块自带
        If Not String.IsNullOrWhiteSpace(gamePath) Then
            Dim moduleDir = Path.Combine(gamePath, "Modules", modFolder)
            If Directory.Exists(moduleDir) Then
                For Each baseName In ModuleImageNames
                    For Each ext In ImageExtensions
                        Dim candidate = Path.Combine(moduleDir, baseName & ext)
                        If File.Exists(candidate) Then Return candidate
                    Next
                Next
            End If
        End If

        Return ""
    End Function

    ''' <summary>把用户选的图片复制进 previews 目录，返回提示文字。</summary>
    Public Function WarbandSetModuleArt(modFolder As String, sourceImage As String) As String
        If String.IsNullOrWhiteSpace(modFolder) Then Return "先选一个模块。"
        If Not File.Exists(sourceImage) Then Return "找不到图片文件。"

        Try
            Directory.CreateDirectory(WarbandPreviewDir)
            '先清掉旧图，避免多扩展名并存导致取到旧的
            WarbandClearModuleArt(modFolder, quiet:=True)

            Dim ext = Path.GetExtension(sourceImage).ToLowerInvariant()
            If Array.IndexOf(ImageExtensions, ext) < 0 Then Return "只支持 png / jpg / webp / bmp。"
            Dim target = Path.Combine(WarbandPreviewDir, modFolder & ext)
            File.Copy(sourceImage, target, True)
            Logger.Info($"[Warband] 已设置模块预览图：{modFolder} -> {target}")
            Return "预览图已更新"
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 设置预览图失败")
            Return "设置失败：" & ex.Message
        End Try
    End Function

    Public Sub WarbandClearModuleArt(modFolder As String, Optional quiet As Boolean = False)
        If String.IsNullOrWhiteSpace(modFolder) Then Return
        Try
            If Not Directory.Exists(WarbandPreviewDir) Then Return
            For Each ext In ImageExtensions
                Dim target = Path.Combine(WarbandPreviewDir, modFolder & ext)
                If File.Exists(target) Then File.Delete(target)
            Next
            If Not quiet Then Logger.Info($"[Warband] 已清除模块预览图：{modFolder}")
        Catch ex As Exception
            Logger.Warn(ex, "[Warband] 清除预览图失败")
        End Try
    End Sub

End Module
